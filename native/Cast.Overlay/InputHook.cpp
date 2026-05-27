#include "pch.h"
#include "InputHook.h"
#include "Overlay.h"
#include "ControlChannel.h"
#include "Logger.h"
#include <MinHook.h>
#include <windowsx.h>
#include <atomic>

namespace cast::overlay {
    namespace {

        std::atomic<bool> g_active{ false };
        HWND    g_targetWnd = nullptr;
        WNDPROC g_origWndProc = nullptr;

        // Горячая клавиша оверлея
        constexpr int kToggleKey = VK_F8;

        // --- Хуки курсорных API --------------------------------------------
        // Игры с обзором мышью каждый кадр возвращают курсор в центр
        // через SetCursorPos и/или зажимают его ClipCursor. Подклассинг WndProc
        // это не перехватывает (API дёргаются напрямую), поэтому пока оверлей
        // открыт, перехватываем сами функции и делаем их no-op — курсор
        // остаётся там, куда его привёл пользователь
        using SetCursorPos_t = BOOL(WINAPI*)(int, int);
        using GetCursorPos_t = BOOL(WINAPI*)(LPPOINT);
        using ClipCursor_t = BOOL(WINAPI*)(const RECT*);
        using GetRawInputData_t = UINT(WINAPI*)(HRAWINPUT, UINT, LPVOID, PUINT, UINT);

        SetCursorPos_t g_origSetCursorPos = nullptr;
        GetCursorPos_t g_origGetCursorPos = nullptr;
        ClipCursor_t   g_origClipCursor = nullptr;
        GetRawInputData_t g_origGetRawInputData = nullptr;

        bool           g_cursorApiHooked = false;
        POINT          g_lockedCursorPos = { 0, 0 };

        BOOL WINAPI HookedSetCursorPos(int x, int y)
        {
            if (IsVisible()) {
                // Игра пытается отцентрировать курсор. 
                // Запоминаем этот "центр", чтобы потом отправить в GetCursorPos
                g_lockedCursorPos.x = x;
                g_lockedCursorPos.y = y;
                return TRUE; // Блокируем перемещение курсора в Windows
            }
            return g_origSetCursorPos ? g_origSetCursorPos(x, y) : FALSE;
        }

        BOOL WINAPI HookedGetCursorPos(LPPOINT lpPoint)
        {
            if (IsVisible() && lpPoint) {
                *lpPoint = g_lockedCursorPos;
                return TRUE;
            }
            return g_origGetCursorPos ? g_origGetCursorPos(lpPoint) : FALSE;
        }

        BOOL WINAPI HookedClipCursor(const RECT* rect)
        {
            if (IsVisible())
                return g_origClipCursor ? g_origClipCursor(nullptr) : FALSE;
            return g_origClipCursor ? g_origClipCursor(rect) : FALSE;
        }

        // Хук на Raw Input
        UINT WINAPI HookedGetRawInputData(HRAWINPUT hRawInput, UINT uiCommand, LPVOID pData, PUINT pcbSize, UINT cbSizeHeader)
        {
            UINT ret = g_origGetRawInputData ? g_origGetRawInputData(hRawInput, uiCommand, pData, pcbSize, cbSizeHeader) : 0;

            // Если оверлей видим, запрос успешен и игра запрашивает сами данные (RID_INPUT)
            if (IsVisible() && pData && ret != (UINT)-1 && uiCommand == RID_INPUT)
            {
                RAWINPUT* raw = static_cast<RAWINPUT*>(pData);

                if (raw->header.dwType == RIM_TYPEMOUSE)
                {
                    // Обнуляем дельту движения (чтобы камера не вращалась)
                    raw->data.mouse.lLastX = 0;
                    raw->data.mouse.lLastY = 0;
                    // Обнуляем нажатия кнопок мыши (чтобы оружие не стреляло)
                    raw->data.mouse.usButtonFlags = 0;
                }
                else if (raw->header.dwType == RIM_TYPEKEYBOARD)
                {
                    raw->data.keyboard.MakeCode = 0;
                    raw->data.keyboard.VKey = 0;
                }
            }
            return ret;
        }

        void InstallCursorApiHooks()
        {
            if (g_cursorApiHooked)
                return;

            const bool ok =
                MH_CreateHookApi(L"user32", "SetCursorPos", &HookedSetCursorPos, reinterpret_cast<void**>(&g_origSetCursorPos)) == MH_OK &&
                MH_CreateHookApi(L"user32", "GetCursorPos", &HookedGetCursorPos, reinterpret_cast<void**>(&g_origGetCursorPos)) == MH_OK &&
                MH_CreateHookApi(L"user32", "ClipCursor", &HookedClipCursor, reinterpret_cast<void**>(&g_origClipCursor)) == MH_OK &&
                MH_CreateHookApi(L"user32", "GetRawInputData", &HookedGetRawInputData, reinterpret_cast<void**>(&g_origGetRawInputData)) == MH_OK;

            if (!ok)
            {
                CAST_LOG_WARN("Не удалось создать хуки курсорных API");
                return;
            }

            MH_EnableHook(MH_ALL_HOOKS);
            g_cursorApiHooked = true;
            CAST_LOG_INFO("Курсорные хуки установлены (включая Raw Input)");
        }

        int g_cursorIncrements = 0;

        void ShowSystemCursor()
        {
            ClipCursor(nullptr);
            int count = ShowCursor(TRUE);
            ++g_cursorIncrements;
            while (count < 0)
            {
                count = ShowCursor(TRUE);
                ++g_cursorIncrements;
            }
        }

        void RestoreCursor()
        {
            while (g_cursorIncrements > 0)
            {
                ShowCursor(FALSE);
                --g_cursorIncrements;
            }
        }

        // Синтезирует WM_KEYUP для всех зажатых клавиш в исходный WndProc игры
        void ReleaseHeldKeys()
        {
            if (!g_origWndProc || !g_targetWnd)
                return;
            BYTE state[256];
            if (!GetKeyboardState(state))
                return;
            for (int vk = 0; vk < 256; ++vk)
            {
                if (state[vk] & 0x80)
                {
                    const UINT scan = MapVirtualKeyW(static_cast<UINT>(vk), MAPVK_VK_TO_VSC);
                    const LPARAM lParam = (static_cast<LPARAM>(scan) << 16) | 0xC0000001;
                    CallWindowProcW(g_origWndProc, g_targetWnd, WM_KEYUP,
                        static_cast<WPARAM>(vk), lParam);
                }
            }
        }

        bool IsInputMessage(UINT msg)
        {
            switch (msg)
            {
            case WM_MOUSEMOVE:
            case WM_LBUTTONDOWN: case WM_LBUTTONUP: case WM_LBUTTONDBLCLK:
            case WM_RBUTTONDOWN: case WM_RBUTTONUP: case WM_RBUTTONDBLCLK:
            case WM_MBUTTONDOWN: case WM_MBUTTONUP: case WM_MBUTTONDBLCLK:
            case WM_XBUTTONDOWN: case WM_XBUTTONUP: case WM_XBUTTONDBLCLK:
            case WM_MOUSEWHEEL:  case WM_MOUSEHWHEEL:
                // клавиатура
            case WM_KEYDOWN:  case WM_KEYUP:
            case WM_SYSKEYDOWN: case WM_SYSKEYUP:
            case WM_CHAR: case WM_SYSCHAR: case WM_DEADCHAR: case WM_UNICHAR:
                // raw input (DirectInput/RawInput)
            case WM_INPUT:
                return true;
            default:
                return false;
            }
        }

        // Текущее состояние модификаторов
        uint32_t CurrentModifiers()
        {
            uint32_t m = 0;
            if (GetKeyState(VK_SHIFT) & 0x8000)   m |= ipc::kModShift;
            if (GetKeyState(VK_CONTROL) & 0x8000) m |= ipc::kModCtrl;
            if (GetKeyState(VK_MENU) & 0x8000)    m |= ipc::kModAlt;
            if (GetKeyState(VK_LBUTTON) & 0x8000) m |= ipc::kModLeftBtn;
            if (GetKeyState(VK_MBUTTON) & 0x8000) m |= ipc::kModMiddleBtn;
            if (GetKeyState(VK_RBUTTON) & 0x8000) m |= ipc::kModRightBtn;
            return m;
        }

        // Транслирует оконное сообщение ввода в событие обратного канала и кладёт
        // его в кольцо для CEF-хоста. Координаты — клиентские пиксели окна
        // (после resize в хосте совпадают с координатами кадра CEF)
        void TranslateAndPush(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam)
        {
            ipc::InputEvent ev = {};
            ev.modifiers = CurrentModifiers();

            switch (msg)
            {
            case WM_MOUSEMOVE:
                ev.type = ipc::kInputMouseMove;
                ev.x = GET_X_LPARAM(lParam);
                ev.y = GET_Y_LPARAM(lParam);
                break;

            case WM_LBUTTONDOWN: case WM_LBUTTONUP:
            case WM_RBUTTONDOWN: case WM_RBUTTONUP:
            case WM_MBUTTONDOWN: case WM_MBUTTONUP:
                ev.type = (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN || msg == WM_MBUTTONDOWN)
                    ? ipc::kInputMouseDown : ipc::kInputMouseUp;
                ev.x = GET_X_LPARAM(lParam);
                ev.y = GET_Y_LPARAM(lParam);
                ev.button = (msg == WM_LBUTTONDOWN || msg == WM_LBUTTONUP) ? ipc::kMouseLeft
                    : (msg == WM_RBUTTONDOWN || msg == WM_RBUTTONUP) ? ipc::kMouseRight
                    : ipc::kMouseMiddle;
                break;

            case WM_MOUSEWHEEL:
            {
                ev.type = ipc::kInputMouseWheel;
                ev.wheelDelta = GET_WHEEL_DELTA_WPARAM(wParam);
                POINT p{ GET_X_LPARAM(lParam), GET_Y_LPARAM(lParam) };
                ScreenToClient(hwnd, &p);
                ev.x = p.x;
                ev.y = p.y;
                break;
            }

            case WM_KEYDOWN: case WM_SYSKEYDOWN:
                ev.type = ipc::kInputKeyDown;
                ev.key = static_cast<uint32_t>(wParam);
                break;

            case WM_KEYUP: case WM_SYSKEYUP:
                ev.type = ipc::kInputKeyUp;
                ev.key = static_cast<uint32_t>(wParam);
                break;

            case WM_CHAR: case WM_SYSCHAR:
                ev.type = ipc::kInputChar;
                ev.key = static_cast<uint32_t>(wParam);
                break;

            default:
                return;
            }

            ControlChannel::PushInput(ev);
        }

        LRESULT CALLBACK HookedWndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam)
        {
            if (msg == WM_KEYDOWN && wParam == kToggleKey && (lParam & (1 << 30)) == 0)
            {
                ToggleVisible();
                return 0;
            }

            if (IsVisible())
            {
                if (msg == WM_SETCURSOR)
                {
                    SetCursor(LoadCursorW(nullptr, IDC_ARROW));
                    return TRUE;
                }

                if (IsInputMessage(msg))
                {
                    TranslateAndPush(hwnd, msg, wParam, lParam);
                    return 0;
                }
            }

            return CallWindowProcW(g_origWndProc, hwnd, msg, wParam, lParam);
        }

    }

    bool InputHook::Install(HWND hwnd)
    {
        if (g_active.load(std::memory_order_relaxed))
            return true;
        if (!hwnd || !IsWindow(hwnd))
        {
            CAST_LOG_WARN("InputHook::Install — некорректный HWND");
            return false;
        }

        g_origWndProc = reinterpret_cast<WNDPROC>(
            SetWindowLongPtrW(hwnd, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(&HookedWndProc)));
        if (!g_origWndProc)
        {
            CAST_LOG_ERROR("SetWindowLongPtr(GWLP_WNDPROC) failed: {}", GetLastError());
            return false;
        }

        g_targetWnd = hwnd;
        g_active.store(true, std::memory_order_release);
        CAST_LOG_INFO("InputHook installed (hwnd={})", reinterpret_cast<void*>(hwnd));

        InstallCursorApiHooks();

        if (IsVisible())
        {
            ShowSystemCursor();
            ReleaseHeldKeys();
        }
        return true;
    }

    void InputHook::Remove()
    {
        if (!g_active.exchange(false, std::memory_order_acq_rel))
            return;

        RestoreCursor();
        if (g_targetWnd && IsWindow(g_targetWnd) && g_origWndProc)
            SetWindowLongPtrW(g_targetWnd, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(g_origWndProc));

        g_targetWnd = nullptr;
        g_origWndProc = nullptr;
        CAST_LOG_INFO("InputHook removed");
    }

    bool InputHook::IsActive()
    {
        return g_active.load(std::memory_order_acquire);
    }

    void InputHook::OnVisibilityChanged(bool visible)
    {
        if (!g_active.load(std::memory_order_acquire))
            return;

        if (visible)
        {
            if (g_origGetCursorPos) g_origGetCursorPos(&g_lockedCursorPos);
            else GetCursorPos(&g_lockedCursorPos);

            ShowSystemCursor();
            ReleaseHeldKeys();
        }
        else
            RestoreCursor();
    }
}