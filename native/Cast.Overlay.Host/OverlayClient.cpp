#include "OverlayClient.h"
#include "include/wrapper/cef_helpers.h"
#include "include/cef_app.h"
#include "include/cef_browser.h"
#include "include/cef_task.h"
#include <string>

namespace cast {
    namespace {

        class PollTask : public CefTask {
        public:
            explicit PollTask(CefRefPtr<OverlayClient> client) : client_(client) {}
            void Execute() override { client_->PollControl(); }
        private:
            CefRefPtr<OverlayClient> client_;
            IMPLEMENT_REFCOUNTING(PollTask);
        };

        // Период опроса (~60 Гц)
        constexpr int64_t kPollIntervalMs = 16;

    }

    OverlayClient::OverlayClient(uint32_t gamePid, int width, int height)
        : m_gamePid(gamePid), m_width(width), m_height(height)
    {
        m_writer.Open(gamePid);
        // Следим за жизнью процесса игры, чтобы завершить хост вместе с ней
        if (gamePid != 0)
            m_gameProcess = OpenProcess(SYNCHRONIZE, FALSE, gamePid);
    }

    OverlayClient::~OverlayClient()
    {
        if (m_gameProcess)
        {
            CloseHandle(m_gameProcess);
            m_gameProcess = nullptr;
        }
    }

    void OverlayClient::GetViewRect(CefRefPtr<CefBrowser>, CefRect& rect)
    {
        rect = CefRect(0, 0, m_width, m_height);
    }

    bool OverlayClient::GetScreenInfo(CefRefPtr<CefBrowser>, CefScreenInfo& info)
    {
        info.device_scale_factor = 1.0f;
        info.depth = 32;
        info.depth_per_component = 8;
        info.is_monochrome = false;
        info.rect = CefRect(0, 0, m_width, m_height);
        info.available_rect = info.rect;
        return true;
    }

    void OverlayClient::OnPaint(CefRefPtr<CefBrowser>, PaintElementType type,
        const RectList&, const void* buffer,
        int width, int height)
    {
        if (type != PET_VIEW)
            return;

        m_writer.Write(buffer, static_cast<uint32_t>(width),
            static_cast<uint32_t>(height),
            static_cast<uint32_t>(width) * 4);
    }

    void OverlayClient::OnAfterCreated(CefRefPtr<CefBrowser> browser)
    {
        CEF_REQUIRE_UI_THREAD();
        m_browser = browser;
        CefPostDelayedTask(TID_UI, new PollTask(this), kPollIntervalMs);
    }

    void OverlayClient::OnBeforeClose(CefRefPtr<CefBrowser>)
    {
        CEF_REQUIRE_UI_THREAD();
        m_browser = nullptr;
        m_control.Close();
        CefQuitMessageLoop();
    }

    bool OverlayClient::OnBeforePopup(CefRefPtr<CefBrowser> browser,
        CefRefPtr<CefFrame> frame,
        int popup_id,
        const CefString& target_url,
        const CefString& target_frame_name,
        WindowOpenDisposition target_disposition,
        bool user_gesture,
        const CefPopupFeatures& popupFeatures,
        CefWindowInfo& windowInfo,
        CefRefPtr<CefClient>& client,
        CefBrowserSettings& settings,
        CefRefPtr<CefDictionaryValue>& extra_info,
        bool* no_javascript_access)
    {
        CEF_REQUIRE_UI_THREAD();
        // Не создаём отдельное окно/внешнее приложение — навигируем ту же
        // страницу на целевой URL (если он есть)
        if (!target_url.empty())
        {
            CefRefPtr<CefFrame> target = frame ? frame
                : (browser ? browser->GetMainFrame() : nullptr);
            if (target)
                target->LoadURL(target_url);
        }
        return true;
    }

    void OverlayClient::PollControl()
    {
        CEF_REQUIRE_UI_THREAD();
        if (!m_browser)
            return; // браузер закрыт — цикл больше не перезапускаем

        // Игра завершилась — закрываем браузер (OnBeforeClose остановит цикл
        // сообщений), чтобы хост не превращался в зомби-процесс
        if (m_gameProcess && WaitForSingleObject(m_gameProcess, 0) == WAIT_OBJECT_0)
        {
            if (CefRefPtr<CefBrowserHost> host = m_browser->GetHost())
                host->CloseBrowser(/*force_close*/ true);
            return; // не перезапускаем опрос
        }

        // DLL могла подняться позже хоста — пробуем открыть отображение лениво
        if (!m_control.IsOpen())
            m_control.Open(m_gamePid);

        if (m_control.IsOpen())
        {
            // Размер игры -> подгоняем off-screen рендер 1:1
            uint32_t w = 0, h = 0;
            if (m_control.ReadSize(w, h) &&
                (static_cast<int>(w) != m_width || static_cast<int>(h) != m_height))
            {
                m_width = static_cast<int>(w);
                m_height = static_cast<int>(h);
                if (m_browser->GetHost())
                    m_browser->GetHost()->WasResized(); // CEF перечитает GetViewRect
            }

            // Фокус ввода в CEF. Раньше выставлялся только по фронту открытия,
            // из-за чего после Alt+Tab и возврата в игру фокус не возвращался и
            // мышь/клавиатура в оверлее «пропадали». Теперь, пока оверлей открыт,
            // удерживаем фокус на каждом опросе — повторный SetFocus(true) дёшев и
            // идемпотентен, поэтому ввод восстанавливается сразу после возврата
            const bool visible = m_control.OverlayVisible();
            if (visible && m_browser->GetHost())
                m_browser->GetHost()->SetFocus(true);

            // Пушим состояние open/closed на страницу при смене — React-страница
            // переключается между подсказкой-тостом (закрыт) и полным UI (открыт)
            if (visible != m_lastVisible)
            {
                if (CefRefPtr<CefFrame> frame = m_browser->GetMainFrame())
                {
                    const char* b = visible ? "true" : "false";
                    std::string js =
                        std::string("window.__castOverlayOpen=") + b +
                        ";window.dispatchEvent(new CustomEvent('cast-overlay',{detail:{open:" + b + "}}));";
                    frame->ExecuteJavaScript(js, frame->GetURL(), 0);
                }
            }
            m_lastVisible = visible;

            // События ввода -> в браузер. Ограничиваем число за один проход,
            // чтобы поток поллинга не зацикливался при флуде move-событий
            ipc::InputEvent ev;
            int guard = 0;
            while (guard++ < 512 && m_control.PopInput(ev))
                DispatchInput(ev);
        }

        CefPostDelayedTask(TID_UI, new PollTask(this), kPollIntervalMs);
    }

    void OverlayClient::DispatchInput(const ipc::InputEvent& ev)
    {
        CefRefPtr<CefBrowserHost> host = m_browser ? m_browser->GetHost() : nullptr;
        if (!host)
            return;

        uint32_t mods = 0;
        if (ev.modifiers & ipc::kModShift)     mods |= EVENTFLAG_SHIFT_DOWN;
        if (ev.modifiers & ipc::kModCtrl)      mods |= EVENTFLAG_CONTROL_DOWN;
        if (ev.modifiers & ipc::kModAlt)       mods |= EVENTFLAG_ALT_DOWN;
        if (ev.modifiers & ipc::kModLeftBtn)   mods |= EVENTFLAG_LEFT_MOUSE_BUTTON;
        if (ev.modifiers & ipc::kModMiddleBtn) mods |= EVENTFLAG_MIDDLE_MOUSE_BUTTON;
        if (ev.modifiers & ipc::kModRightBtn)  mods |= EVENTFLAG_RIGHT_MOUSE_BUTTON;

        switch (ev.type)
        {
        case ipc::kInputMouseMove:
        {
            m_lastMouseX = ev.x; m_lastMouseY = ev.y;
            CefMouseEvent me; me.x = ev.x; me.y = ev.y; me.modifiers = mods;
            host->SendMouseMoveEvent(me, /*mouseLeave*/ false);
            break;
        }
        case ipc::kInputMouseDown:
        case ipc::kInputMouseUp:
        {
            m_lastMouseX = ev.x; m_lastMouseY = ev.y;
            CefMouseEvent me; me.x = ev.x; me.y = ev.y; me.modifiers = mods;
            cef_mouse_button_type_t btn = MBT_LEFT;
            if (ev.button == ipc::kMouseRight)       btn = MBT_RIGHT;
            else if (ev.button == ipc::kMouseMiddle) btn = MBT_MIDDLE;
            host->SendMouseClickEvent(me, btn, ev.type == ipc::kInputMouseUp, 1);
            break;
        }
        case ipc::kInputMouseWheel:
        {
            CefMouseEvent me; me.x = ev.x; me.y = ev.y; me.modifiers = mods;
            host->SendMouseWheelEvent(me, /*deltaX*/ 0, /*deltaY*/ ev.wheelDelta);
            break;
        }
        case ipc::kInputKeyDown:
        case ipc::kInputKeyUp:
        {
            CefKeyEvent ke;
            ke.modifiers = mods;
            ke.windows_key_code = static_cast<int>(ev.key);
            ke.native_key_code = 0;
            ke.type = (ev.type == ipc::kInputKeyDown) ? KEYEVENT_RAWKEYDOWN : KEYEVENT_KEYUP;
            host->SendKeyEvent(ke);
            break;
        }
        case ipc::kInputChar:
        {
            CefKeyEvent ke;
            ke.modifiers = mods;
            ke.type = KEYEVENT_CHAR;
            ke.windows_key_code = static_cast<int>(ev.key);
            ke.character = static_cast<char16_t>(ev.key);
            ke.unmodified_character = static_cast<char16_t>(ev.key);
            host->SendKeyEvent(ke);
            break;
        }
        default:
            break;
        }
    }
}