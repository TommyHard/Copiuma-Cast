#include "pch.h"
#include "D3D9Hook.h"
#include "D3D9Renderer.h"
#include "Overlay.h"
#include "InputHook.h"
#include "ControlChannel.h"
#include "Logger.h"
#include <d3d9.h>
#include <MinHook.h>

#pragma comment(lib, "d3d9.lib")

namespace cast {
    namespace {

        constexpr int kVTableIndex_Reset = 16;
        constexpr int kVTableIndex_EndScene = 42;

        using EndScene_t = HRESULT(STDMETHODCALLTYPE*)(IDirect3DDevice9*);
        using Reset_t = HRESULT(STDMETHODCALLTYPE*)(IDirect3DDevice9*, D3DPRESENT_PARAMETERS*);

        EndScene_t   g_origEndScene = nullptr;
        Reset_t      g_origReset = nullptr;
        D3D9Renderer g_renderer;

        // SEH-обёртки. В модифицированных сборках (ENB, ASI-хуки, d3d9-прокси)
        // рендер или создание устройства могут упасть из-за чужого состояния.
        // Ловим аппаратное исключение, чтобы залогировать и НЕ уронить игру
        HRESULT SafeCreateDevice(IDirect3D9* d3d, HWND wnd, DWORD flags,
            D3DPRESENT_PARAMETERS* pp, IDirect3DDevice9** out)
        {
            __try { return d3d->CreateDevice(D3DADAPTER_DEFAULT, D3DDEVTYPE_HAL, wnd, flags, pp, out); }
            __except (EXCEPTION_EXECUTE_HANDLER) { return E_FAIL; }
        }

        bool SafeRenderInit(IDirect3DDevice9* device)
        {
            __try { return g_renderer.Initialize(device); }
            __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
        }

        bool SafeRender(IDirect3DDevice9* device)
        {
            __try { g_renderer.Render(device); return true; }
            __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
        }

        // Ищет главное окно текущего процесса (окно игры)
        BOOL CALLBACK EnumWndProc(HWND hwnd, LPARAM lParam)
        {
            DWORD pid = 0;
            GetWindowThreadProcessId(hwnd, &pid);
            if (pid == GetCurrentProcessId() &&
                GetWindow(hwnd, GW_OWNER) == nullptr &&  // top-level окно
                IsWindowVisible(hwnd))
            {
                *reinterpret_cast<HWND*>(lParam) = hwnd;
                return FALSE;
            }
            return TRUE;
        }

        HWND FindGameWindow()
        {
            HWND found = nullptr;
            EnumWindows(EnumWndProc, reinterpret_cast<LPARAM>(&found));
            return found;
        }

        HWND g_gameWnd = nullptr;

        HWND ResolveGameWindow(IDirect3DDevice9* device)
        {
            if (g_gameWnd && IsWindow(g_gameWnd))
                return g_gameWnd;

            HWND hwnd = nullptr;
            D3DDEVICE_CREATION_PARAMETERS cp = {};
            if (SUCCEEDED(device->GetCreationParameters(&cp)))
                hwnd = cp.hFocusWindow;
            if (!hwnd) hwnd = FindGameWindow();
            if (!hwnd) hwnd = GetForegroundWindow();
            g_gameWnd = hwnd;
            return hwnd;
        }

        // Сообщает хосту размер КЛИЕНТСКОЙ ОБЛАСТИ окна игры. В этих
        // координатах WndProc отдаёт позицию мыши, поэтому CEF-страница должна
        // рендериться в этом же размере — тогда клики совпадают пиксель-в-пиксель
        // и масштаб ровно 1:1
        void MaybeReportGameSize(IDirect3DDevice9* device)
        {
            HWND hwnd = ResolveGameWindow(device);
            if (!hwnd)
                return;
            RECT rc = {};
            if (GetClientRect(hwnd, &rc))
            {
                const uint32_t w = static_cast<uint32_t>(rc.right - rc.left);
                const uint32_t h = static_cast<uint32_t>(rc.bottom - rc.top);
                overlay::ControlChannel::SetGameSize(w, h);
            }
        }

        // Один раз ставит WndProc-хук на окно игры. Делаем это в EndScene,
        // т.к. этот код исполняется в потоке игры, который владеет окном —
        // корректный поток для SetWindowLongPtr
        void EnsureInputHook(IDirect3DDevice9* device)
        {
            if (overlay::InputHook::IsActive())
                return;
            if (HWND hwnd = ResolveGameWindow(device))
                overlay::InputHook::Install(hwnd);
        }

        // Хук EndScene: вызывается игрой в конце каждого кадра
        HRESULT STDMETHODCALLTYPE HookedEndScene(IDirect3DDevice9* device)
        {
            static bool s_renderFault = false;

            if (device->TestCooperativeLevel() != D3D_OK) 
            {
                return g_origEndScene(device);
            }

            EnsureInputHook(device);
            MaybeReportGameSize(device);

            if (!s_renderFault && !g_renderer.IsInitialized())
            {
                if (!SafeRenderInit(device))
                {
                    s_renderFault = true;
                    CAST_LOG_ERROR("Renderer init faulted (modded env?) — overlay drawing disabled");
                }
            }

            if (overlay::IsVisible() && g_renderer.IsInitialized() && !s_renderFault)
            {
                if (!SafeRender(device))
                {
                    s_renderFault = true;
                    CAST_LOG_ERROR("Render faulted — overlay drawing disabled to keep game alive");
                }
            }

            return g_origEndScene(device);
        }

        // Хук Reset: устройство пересоздаётся (смена разрешения, выход из Alt-Tab)
        HRESULT STDMETHODCALLTYPE HookedReset(IDirect3DDevice9* device, D3DPRESENT_PARAMETERS* params)
        {
            g_renderer.OnLostDevice();          // освобождаем ресурсы ДО reset
            const HRESULT hr = g_origReset(device, params);
            if (SUCCEEDED(hr))
                g_renderer.OnResetDevice(device); // восстанавливаем ПОСЛЕ
            return hr;
        }

        // Создаёт временное устройство и достаёт адреса EndScene и Reset из его vtable
        bool AcquireDeviceMethods(void** outEndScene, void** outReset)
        {
            WNDCLASSEXW wc = { sizeof(WNDCLASSEXW) };
            wc.lpfnWndProc = DefWindowProcW;
            wc.hInstance = GetModuleHandleW(nullptr);
            wc.lpszClassName = L"CastOverlayDummyWnd";
            RegisterClassExW(&wc);

            HWND fallbackWnd = CreateWindowExW(0, wc.lpszClassName, L"", WS_OVERLAPPEDWINDOW,
                0, 0, 1, 1, nullptr, nullptr, wc.hInstance, nullptr);

            HWND targetWnd = FindGameWindow();
            if (!targetWnd) targetWnd = GetForegroundWindow();
            if (!targetWnd) targetWnd = fallbackWnd;

            bool ok = false;
            if (IDirect3D9* d3d = Direct3DCreate9(D3D_SDK_VERSION))
            {
                D3DDISPLAYMODE dm = {};
                d3d->GetAdapterDisplayMode(D3DADAPTER_DEFAULT, &dm);

                D3DPRESENT_PARAMETERS pp = {};
                pp.Windowed = TRUE;
                pp.SwapEffect = D3DSWAPEFFECT_DISCARD;
                pp.hDeviceWindow = targetWnd;
                pp.BackBufferWidth = 1;
                pp.BackBufferHeight = 1;
                pp.BackBufferFormat = dm.Format;

                const DWORD behaviors[] = {
                    D3DCREATE_HARDWARE_VERTEXPROCESSING,
                    D3DCREATE_SOFTWARE_VERTEXPROCESSING,
                    D3DCREATE_MIXED_VERTEXPROCESSING,
                };

                IDirect3DDevice9* dummy = nullptr;
                HRESULT hr = D3DERR_INVALIDCALL;
                for (DWORD flags : behaviors)
                {
                    hr = SafeCreateDevice(d3d, targetWnd, flags, &pp, &dummy);
                    if (SUCCEEDED(hr))
                        break;
                }

                if (SUCCEEDED(hr) && dummy)
                {
                    void** vtbl = *reinterpret_cast<void***>(dummy);
                    *outEndScene = vtbl[kVTableIndex_EndScene];
                    *outReset = vtbl[kVTableIndex_Reset];
                    dummy->Release();
                    ok = true;
                }
                else
                {
                    CAST_LOG_ERROR("CreateDevice (dummy) failed: 0x{:08X} (targetWnd={})",
                        static_cast<uint32_t>(hr), reinterpret_cast<void*>(targetWnd));
                }
                d3d->Release();
            }
            else
            {
                CAST_LOG_ERROR("Direct3DCreate9 failed");
            }

            if (fallbackWnd) DestroyWindow(fallbackWnd);
            UnregisterClassW(wc.lpszClassName, wc.hInstance);
            return ok;
        }

    }

    bool D3D9Hook::Initialize()
    {
        void* pEndScene = nullptr;
        void* pReset = nullptr;
        if (!AcquireDeviceMethods(&pEndScene, &pReset))
        {
            MH_Uninitialize();
            return false;
        }

        if (MH_CreateHook(pEndScene, &HookedEndScene, reinterpret_cast<void**>(&g_origEndScene)) != MH_OK ||
            MH_CreateHook(pReset, &HookedReset, reinterpret_cast<void**>(&g_origReset)) != MH_OK)
        {
            CAST_LOG_ERROR("MH_CreateHook failed");
            MH_Uninitialize();
            return false;
        }

        if (MH_EnableHook(MH_ALL_HOOKS) != MH_OK)
        {
            CAST_LOG_ERROR("MH_EnableHook failed");
            MH_Uninitialize();
            return false;
        }

        CAST_LOG_INFO("D3D9 hooks installed (EndScene + Reset)");
        return true;
    }

    void D3D9Hook::Shutdown()
    {
        MH_DisableHook(MH_ALL_HOOKS);
        MH_Uninitialize();
        g_renderer.Release();
        CAST_LOG_INFO("D3D9 hooks removed");
    }

}