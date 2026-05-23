#include "pch.h"
#include "Overlay.h"
#include "Logger.h"
#include "D3D9Hook.h"
#include "InputHook.h"
#include "DInputHook.h"
#include "ControlChannel.h"

#include <atomic>
#include <thread>

namespace cast::overlay {
    namespace {

        // Стартуем видимым, чтобы сразу проверить, что хук рисует.
        // Позже значение по умолчанию станет false (оверлей вызывается по клавише)
        std::atomic<bool> g_visible{ true };

        std::atomic<bool> g_running{ false };
        std::thread       g_hotkeyThread;

        // опрос F8 — работает только в окне между inject и установкой
        // WndProc-хука (хук ставится из EndScene при первом кадре). Как только
        // InputHook активен, он становится единственным владельцем горячей
        // клавиши, а этот цикл перестаёт переключать видимость, чтобы F8 не
        // обрабатывалась дважды
        constexpr int kToggleKey = VK_F8;

        void HotkeyLoop()
        {
            bool prevDown = false;
            while (g_running.load(std::memory_order_relaxed))
            {
                if (!InputHook::IsActive())
                {
                    const bool down = (GetAsyncKeyState(kToggleKey) & 0x8000) != 0;
                    if (down && !prevDown)      // срабатываем по фронту нажатия
                        ToggleVisible();
                    prevDown = down;
                }
                Sleep(25);
            }
        }

    }

    void Initialize()
    {
        Logger::Init();
        CAST_LOG_INFO("Overlay::Initialize");

        if (!D3D9Hook::Initialize())
        {
            CAST_LOG_ERROR("D3D9Hook::Initialize failed — overlay disabled");
            return;
        }

        // Обратный канал (DLL -> хост): размер игры, видимость, события ввода
        ControlChannel::Open();
        ControlChannel::SetOverlayVisible(g_visible.load(std::memory_order_relaxed));

        // Блокировка ввода игры через DirectInput8 (обзор камерой/клавиатура),
        // пока оверлей открыт. Не критично, если игра не использует DInput
        DInputHook::Initialize();

        g_running.store(true, std::memory_order_relaxed);
        g_hotkeyThread = std::thread(HotkeyLoop);

        CAST_LOG_INFO("Overlay ready (toggle = F8)");
    }

    void Shutdown()
    {
        CAST_LOG_INFO("Overlay::Shutdown");

        g_running.store(false, std::memory_order_relaxed);
        if (g_hotkeyThread.joinable())
            g_hotkeyThread.join();

        InputHook::Remove();   // восстанавливаем исходный WndProc окна игры
        D3D9Hook::Shutdown();  // снимает все MinHook-хуки (D3D9 + курсор + DInput)
        ControlChannel::Close();
        Logger::Shutdown();
    }

    bool IsVisible() { return g_visible.load(std::memory_order_relaxed); }

    void SetVisible(bool visible)
    {
        g_visible.store(visible, std::memory_order_relaxed);
        InputHook::OnVisibilityChanged(visible); // курсор + захват/освобождение
        ControlChannel::SetOverlayVisible(visible);
    }

    void ToggleVisible()
    {
        const bool v = !g_visible.load(std::memory_order_relaxed);
        g_visible.store(v, std::memory_order_relaxed);
        InputHook::OnVisibilityChanged(v);
        ControlChannel::SetOverlayVisible(v);
        CAST_LOG_INFO("Overlay visibility = {}", v);
    }

}