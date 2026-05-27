#include "pch.h"
#include "Overlay.h"
#include "Logger.h"
#include "D3D9Hook.h"
#include "InputHook.h"
#include "DInputHook.h"
#include "ControlChannel.h"
#include "IGraphicsBackend.h"
#include "D3D9Backend.h"
#include "D3D11Backend.h"
#include <MinHook.h>
#include <vector>
#include <atomic>
#include <thread>
#include <memory>

namespace cast::overlay {
    namespace {

        // Стартуем видимым, чтобы сразу проверить, что хук рисует
        // Позже значение по умолчанию станет false
        std::atomic<bool> g_visible{ true };

        std::atomic<bool> g_running{ false };
        std::thread       g_hotkeyThread;

        // опрос F8 — работает только в окне между inject и установкой
        // WndProc-хука (хук ставится из EndScene при первом кадре). Как только
        // InputHook активен, он становится единственным владельцем горячей
        // клавиши, а этот цикл перестаёт переключать видимость, чтобы F8 не
        // обрабатывалась дважды
        constexpr int kToggleKey = VK_F8;

        std::vector<std::unique_ptr<IGraphicsBackend>> g_backends;

        void HotkeyLoop()
        {
            bool prevDown = false;
            while (g_running.load(std::memory_order_relaxed))
            {
                if (!InputHook::IsActive())
                {
                    const bool down = (GetAsyncKeyState(kToggleKey) & 0x8000) != 0;
                    if (down && !prevDown)
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

        // Инициализируем MinHook глобально
        MH_STATUS status = MH_Initialize();
        if (status != MH_OK && status != MH_ERROR_ALREADY_INITIALIZED) {
            CAST_LOG_ERROR("MinHook global init failed");
            return;
        }

        // Ставим хуки на ВСЕ найденные графические библиотеки одновременно
        if (GetModuleHandleW(L"dxgi.dll")) {
            CAST_LOG_INFO("DXGI found. Installing D3D11 hooks...");
            auto b11 = std::make_unique<D3D11Backend>();
            if (b11->Initialize()) g_backends.push_back(std::move(b11));
        }

        if (GetModuleHandleW(L"d3d9.dll")) {
            CAST_LOG_INFO("D3D9 found. Installing D3D9 hooks...");
            auto b9 = std::make_unique<D3D9Backend>();
            if (b9->Initialize()) g_backends.push_back(std::move(b9));
        }

        if (g_backends.empty()) {
            CAST_LOG_ERROR("No supported graphics API found.");
            return;
        }

        ControlChannel::Open();
        ControlChannel::SetOverlayVisible(g_visible.load(std::memory_order_relaxed));
        DInputHook::Initialize();

        g_running.store(true, std::memory_order_relaxed);
        g_hotkeyThread = std::thread(HotkeyLoop);

        CAST_LOG_INFO("Overlay ready");
    }

    void Shutdown()
    {
        g_running.store(false, std::memory_order_relaxed);
        if (g_hotkeyThread.joinable()) g_hotkeyThread.join();

        InputHook::Remove();

        // Выключаем все активные хуки
        for (auto& b : g_backends) b->Shutdown();
        g_backends.clear();

        MH_Uninitialize(); // Отключаем MinHook глобально

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