#include "pch.h"
#include "DInputHook.h"
#include "Overlay.h"
#include "Logger.h"

#define DIRECTINPUT_VERSION 0x0800
#include <dinput.h>
#include <MinHook.h>

#pragma comment(lib, "dinput8.lib")
#pragma comment(lib, "dxguid.lib")

namespace cast::overlay {
    namespace {

        // Индексы методов в vtable IDirectInputDevice8 (после IUnknown 0..2):
        //   ... Acquire(7), Unacquire(8), GetDeviceState(9), GetDeviceData(10) ...
        constexpr int kVTableIndex_GetDeviceState = 9;
        constexpr int kVTableIndex_GetDeviceData = 10;

        using GetDeviceState_t = HRESULT(STDMETHODCALLTYPE*)(
            IDirectInputDevice8*, DWORD, LPVOID);
        using GetDeviceData_t = HRESULT(STDMETHODCALLTYPE*)(
            IDirectInputDevice8*, DWORD, LPDIDEVICEOBJECTDATA, LPDWORD, DWORD);

        // Слоты оригиналов: 0 — мышь, 1 — клавиатура (если её методы по другим
        // адресам, чем у мыши). Мышь и клавиатура в dinput8 могут иметь как общий
        // vtable, так и разный — поэтому поддерживаем оба случая.
        GetDeviceState_t g_origState[2] = {};
        GetDeviceData_t  g_origData[2] = {};

        // Шаблонные детуры: каждый инстанс — отдельный адрес функции и свой слот
        // оригинала, что и позволяет хукать два разных адреса одной логикой.
        template <int Slot>
        HRESULT STDMETHODCALLTYPE HookedGetDeviceState(
            IDirectInputDevice8* dev, DWORD cbData, LPVOID lpvData)
        {
            const HRESULT hr = g_origState[Slot](dev, cbData, lpvData);
            if (IsVisible() && SUCCEEDED(hr) && lpvData && cbData)
                memset(lpvData, 0, cbData); // ни движения, ни нажатых клавиш/кнопок
            return hr;
        }

        template <int Slot>
        HRESULT STDMETHODCALLTYPE HookedGetDeviceData(
            IDirectInputDevice8* dev, DWORD cbObjData, LPDIDEVICEOBJECTDATA rgdod,
            LPDWORD pdwInOut, DWORD dwFlags)
        {
            const HRESULT hr = g_origData[Slot](dev, cbObjData, rgdod, pdwInOut, dwFlags);
            if (IsVisible() && pdwInOut)
                *pdwInOut = 0; // «событий нет» (буфер при этом уже дренирован оригиналом)
            return hr;
        }

        // Достаёт адреса GetDeviceState/GetDeviceData из vtable устройства guid.
        bool GetDeviceMethods(IDirectInput8* di, REFGUID guid, void** outState, void** outData)
        {
            IDirectInputDevice8* dev = nullptr;
            HRESULT hr = di->CreateDevice(guid, &dev, nullptr);
            if (FAILED(hr) || !dev)
                return false;
            void** vtbl = *reinterpret_cast<void***>(dev);
            *outState = vtbl[kVTableIndex_GetDeviceState];
            *outData = vtbl[kVTableIndex_GetDeviceData];
            dev->Release();
            return true;
        }

        bool HookSlot(int slot, void* pState, void* pData)
        {
            bool ok = false;
            if (slot == 0)
            {
                ok = MH_CreateHook(pState, &HookedGetDeviceState<0>,
                        reinterpret_cast<void**>(&g_origState[0])) == MH_OK &&
                     MH_CreateHook(pData, &HookedGetDeviceData<0>,
                        reinterpret_cast<void**>(&g_origData[0])) == MH_OK;
            }
            else
            {
                ok = MH_CreateHook(pState, &HookedGetDeviceState<1>,
                        reinterpret_cast<void**>(&g_origState[1])) == MH_OK &&
                     MH_CreateHook(pData, &HookedGetDeviceData<1>,
                        reinterpret_cast<void**>(&g_origData[1])) == MH_OK;
            }
            return ok;
        }

    } // namespace

    bool DInputHook::Initialize()
    {
        HMODULE hMod = GetModuleHandleW(L"dinput8.dll");
        if (!hMod) hMod = LoadLibraryW(L"dinput8.dll");
        if (!hMod)
        {
            CAST_LOG_WARN("DInputHook: dinput8.dll недоступна — пропускаем");
            return false;
        }

        using DI8Create_t = HRESULT(WINAPI*)(HINSTANCE, DWORD, REFIID, LPVOID*, LPUNKNOWN);
        auto pCreate = reinterpret_cast<DI8Create_t>(GetProcAddress(hMod, "DirectInput8Create"));
        if (!pCreate)
        {
            CAST_LOG_WARN("DInputHook: DirectInput8Create не найден");
            return false;
        }

        IDirectInput8* di = nullptr;
        HRESULT hr = pCreate(GetModuleHandleW(nullptr), DIRECTINPUT_VERSION,
            IID_IDirectInput8, reinterpret_cast<void**>(&di), nullptr);
        if (FAILED(hr) || !di)
        {
            CAST_LOG_WARN("DInputHook: DirectInput8Create failed 0x{:08X}",
                static_cast<uint32_t>(hr));
            return false;
        }

        // Адреса методов мыши и клавиатуры. В одних реализациях они общие,
        // в других — разные; хукаем уникальные.
        void* mState = nullptr; void* mData = nullptr;
        void* kState = nullptr; void* kData = nullptr;
        const bool haveMouse = GetDeviceMethods(di, GUID_SysMouse, &mState, &mData);
        const bool haveKbd = GetDeviceMethods(di, GUID_SysKeyboard, &kState, &kData);
        di->Release();

        if (!haveMouse && !haveKbd)
        {
            CAST_LOG_WARN("DInputHook: не удалось получить методы устройств");
            return false;
        }

        bool any = false;
        if (haveMouse)
            any |= HookSlot(0, mState, mData);

        // Клавиатуру хукаем отдельным слотом, только если её адреса отличаются
        // от мышиных (иначе MinHook уже покрыл их слотом 0).
        if (haveKbd && (!haveMouse || kState != mState))
            any |= HookSlot(1, kState, kData);

        if (!any)
        {
            CAST_LOG_WARN("DInputHook: MH_CreateHook failed для всех слотов");
            return false;
        }

        MH_EnableHook(MH_ALL_HOOKS); // включает в т.ч. уже созданные хуки (безвредно)
        CAST_LOG_INFO("DInputHook installed (mouse={}, keyboard={})",
            haveMouse, haveKbd);
        return true;
    }

} // namespace cast::overlay
