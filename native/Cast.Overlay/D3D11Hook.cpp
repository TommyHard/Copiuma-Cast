#include "pch.h"
#include "D3D11Hook.h"
#include "D3D11Renderer.h"
#include "Overlay.h"
#include "InputHook.h"
#include "ControlChannel.h"
#include "Logger.h"
#include <d3d11.h>
#include <dxgi.h>
#include <MinHook.h>

namespace cast {
    namespace {

        using Present_t = HRESULT(STDMETHODCALLTYPE*)(IDXGISwapChain*, UINT, UINT);
        using ResizeBuffers_t = HRESULT(STDMETHODCALLTYPE*)(IDXGISwapChain*, UINT, UINT, UINT, DXGI_FORMAT, UINT);

        Present_t g_origPresent = nullptr;
        ResizeBuffers_t g_origResizeBuffers = nullptr;
        D3D11Renderer g_renderer;

        void EnsureInputHook(IDXGISwapChain* swapChain) {
            if (overlay::InputHook::IsActive()) return;
            DXGI_SWAP_CHAIN_DESC desc;
            if (SUCCEEDED(swapChain->GetDesc(&desc)) && desc.OutputWindow) {
                overlay::InputHook::Install(desc.OutputWindow);
                RECT rc;
                if (GetClientRect(desc.OutputWindow, &rc)) {
                    overlay::ControlChannel::SetGameSize(rc.right - rc.left, rc.bottom - rc.top);
                }
            }
        }

        HRESULT STDMETHODCALLTYPE HookedPresent(IDXGISwapChain* swapChain, UINT syncInterval, UINT flags) {
            // Игнорируем тестовые кадры (DXGI_PRESENT_TEST) — система проверяет окклюзию,
            // рисовать в них нельзя, вызовет графические артефакты
            if (flags & DXGI_PRESENT_TEST) {
                return g_origPresent(swapChain, syncInterval, flags);
            }

            EnsureInputHook(swapChain);

            // Рендерим оверлей ВСЕГДА: страница сама показывает либо полный UI
            // (открыт), либо тост-подсказку (закрыт). Перехват ввода завязан на
            // видимость (InputHook), поэтому в закрытом состоянии игра управляема
            {
                // Если игра полностью пересоздала девайс
                if (g_renderer.IsInitialized()) {
                    ID3D11Device* currentDevice = nullptr;
                    if (SUCCEEDED(swapChain->GetDevice(__uuidof(ID3D11Device), (void**)&currentDevice))) {
                        if (currentDevice != g_renderer.GetDevice()) {
                            g_renderer.Release(); // Сбрасываем старый рендер
                        }
                        currentDevice->Release();
                    }
                }

                if (!g_renderer.IsInitialized()) {
                    ID3D11Device* device = nullptr;
                    if (SUCCEEDED(swapChain->GetDevice(__uuidof(ID3D11Device), (void**)&device))) {
                        ID3D11DeviceContext* context = nullptr;
                        device->GetImmediateContext(&context);
                        g_renderer.Initialize(device, context);
                        context->Release();
                        device->Release();
                    }
                }
                g_renderer.Render(swapChain); // Передаем swapChain внутрь
            }
            return g_origPresent(swapChain, syncInterval, flags);
        }

        // Хук изменения разрешения / Alt+Tab
        HRESULT STDMETHODCALLTYPE HookedResizeBuffers(IDXGISwapChain* swapChain, UINT bufferCount, UINT width, UINT height, DXGI_FORMAT newFormat, UINT swapChainFlags) {
            g_renderer.Release(); // Освобождаем ресурсы перед ресайзом
            return g_origResizeBuffers(swapChain, bufferCount, width, height, newFormat, swapChainFlags);
        }

        // Получаем адреса функций из временного SwapChain
        bool AcquireD3D11Methods(void** outPresent, void** outResize) {
            WNDCLASSEXW wc = { sizeof(WNDCLASSEXW), CS_HREDRAW | CS_VREDRAW, DefWindowProcW, 0, 0, GetModuleHandle(nullptr), nullptr, nullptr, nullptr, nullptr, L"DummyD3D11", nullptr };
            RegisterClassExW(&wc);
            HWND hwnd = CreateWindowExW(0, wc.lpszClassName, L"", WS_OVERLAPPEDWINDOW, 0, 0, 100, 100, nullptr, nullptr, wc.hInstance, nullptr);

            DXGI_SWAP_CHAIN_DESC sd = {};
            sd.BufferCount = 1;
            sd.BufferDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
            sd.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
            sd.OutputWindow = hwnd;
            sd.SampleDesc.Count = 1;
            sd.Windowed = TRUE;
            sd.SwapEffect = DXGI_SWAP_EFFECT_DISCARD;

            // Измененный блок внутри AcquireD3D11Methods
            IDXGISwapChain* swapChain = nullptr;
            ID3D11Device* device = nullptr;

            // Добавляем массив совместимости
            D3D_FEATURE_LEVEL featureLevels[] = {
                D3D_FEATURE_LEVEL_11_1, D3D_FEATURE_LEVEL_11_0,
                D3D_FEATURE_LEVEL_10_1, D3D_FEATURE_LEVEL_10_0
            };

            HRESULT hr = D3D11CreateDeviceAndSwapChain(
                nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, 0,
                featureLevels, 4, D3D11_SDK_VERSION, &sd,
                &swapChain, &device, nullptr, nullptr
            );

            bool ok = false;
            if (SUCCEEDED(hr)) {
                void** vtbl = *reinterpret_cast<void***>(swapChain);
                *outPresent = vtbl[8];  // Present - 8 индекс в vtable
                *outResize = vtbl[13];  // ResizeBuffers - 13 индекс
                swapChain->Release();
                device->Release();
                ok = true;
            }

            DestroyWindow(hwnd);
            UnregisterClassW(wc.lpszClassName, wc.hInstance);
            return ok;
        }
    }

    bool D3D11Hook::Initialize() {
        void* pPresent = nullptr;
        void* pResize = nullptr;
        if (!AcquireD3D11Methods(&pPresent, &pResize)) return false;

        MH_CreateHook(pPresent, &HookedPresent, reinterpret_cast<void**>(&g_origPresent));
        MH_CreateHook(pResize, &HookedResizeBuffers, reinterpret_cast<void**>(&g_origResizeBuffers));

        MH_EnableHook(MH_ALL_HOOKS);
        return true;
    }

    void D3D11Hook::Shutdown() {
        MH_DisableHook(MH_ALL_HOOKS);
        g_renderer.Release();
        CAST_LOG_INFO("D3D11 hooks removed");
    }
}