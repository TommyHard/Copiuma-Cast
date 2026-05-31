// OverlayClient.h — CefClient с off-screen рендер-хендлером.
// OnPaint получает BGRA-кадр React-страницы и публикует его в shared memory
// через SharedFrameWriter, откуда его читает DLL оверлея в игре
#pragma once

#include "include/cef_client.h"
#include "include/cef_render_handler.h"
#include "include/cef_life_span_handler.h"

#include "SharedFrameWriter.h"
#include "SharedControlReader.h"

namespace cast {

    class OverlayClient : public CefClient,
        public CefRenderHandler,
        public CefLifeSpanHandler {
    public:
        OverlayClient(uint32_t gamePid, int width, int height);
        ~OverlayClient();

        // CefClient
        CefRefPtr<CefRenderHandler>   GetRenderHandler()   override { return this; }
        CefRefPtr<CefLifeSpanHandler> GetLifeSpanHandler() override { return this; }

        // CefRenderHandler
        void GetViewRect(CefRefPtr<CefBrowser> browser, CefRect& rect) override;
        // Форсируем device_scale_factor = 1.0: в OSR это гарантирует, что
        // координаты мыши (пиксели) трактуются 1:1 как DIP, иначе при DPI 150%
        // CEF умножает их на 1.5 и клики промахиваются
        bool GetScreenInfo(CefRefPtr<CefBrowser> browser, CefScreenInfo& screen_info) override;
        void OnPaint(CefRefPtr<CefBrowser> browser, PaintElementType type,
            const RectList& dirtyRects, const void* buffer,
            int width, int height) override;

        // CefLifeSpanHandler
        void OnAfterCreated(CefRefPtr<CefBrowser> browser) override;
        void OnBeforeClose(CefRefPtr<CefBrowser> browser) override;
        // Подавляем popup/внешние окна: ссылку грузим в той же странице
        bool OnBeforePopup(CefRefPtr<CefBrowser> browser,
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
            bool* no_javascript_access) override;

        // Опрос обратного канала на UI-потоке: применяет размер игры (WasResized)
        // и пробрасывает накопленный ввод в браузер. Перезапускает сам себя
        void PollControl();

    private:
        void DispatchInput(const ipc::InputEvent& ev);

        uint32_t m_gamePid;
        // Хэндл процесса игры (SYNCHRONIZE). Когда игра завершается, опрос это
        // замечает и закрывает браузер — иначе хост остаётся зомби-процессом
        HANDLE   m_gameProcess = nullptr;
        int m_width;
        int m_height;
        SharedFrameWriter     m_writer;
        SharedControlReader   m_control;
        CefRefPtr<CefBrowser> m_browser;
        // Последняя позиция курсора — для координат click/wheel событий,
        // если они придут без свежего move
        int m_lastMouseX = 0;
        int m_lastMouseY = 0;
        // Предыдущее состояние видимости — чтобы по фронту открытия вернуть
        // фокус ввода в CEF (иначе после ухода фокуса в др. окно клавиатура/мышь
        // в оверлее перестают работать)
        bool m_lastVisible = false;

        IMPLEMENT_REFCOUNTING(OverlayClient);
        DISALLOW_COPY_AND_ASSIGN(OverlayClient);
    };
}