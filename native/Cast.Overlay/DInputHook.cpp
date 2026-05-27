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

        constexpr int kVTableIndex_GetDeviceState = 9;
        constexpr int kVTableIndex_GetDeviceData = 10;

        using GetDeviceState_t = HRESULT(STDMETHODCALLTYPE*)(
            IDirectInputDevice8*, DWORD, LPVOID);
        using GetDeviceData_t = HRESULT(STDMETHODCALLTYPE*)(
            IDirectInputDevice8*, DWORD, LPDIDEVICEOBJECTDATA, LPDWORD, DWORD);

        GetDeviceState_t g_origState[2] = {};
        GetDeviceData_t  g_origData[2] = {};

        // Безопасная функция определения типа устройства, обходящая конфликт ANSI/UNICODE
        DWORD GetDeviceTypeSafe(IDirectInputDevice8* dev)
        {
            // Указатель на функцию GetDeviceInfo
            void* pGetDeviceInfo = (*reinterpret_cast<void***>(dev))[15];

            DIDEVICEINSTANCEW instW = {};
            instW.dwSize = sizeof(DIDEVICEINSTANCEW);
            using GetInfoW_t = HRESULT(STDMETHODCALLTYPE*)(IDirectInputDevice8*, DIDEVICEINSTANCEW*);
            if (SUCCEEDED((reinterpret_cast<GetInfoW_t>(pGetDeviceInfo))(dev, &instW))) {
                return GET_DIDEVICE_TYPE(instW.dwDevType);
            }

            DIDEVICEINSTANCEA instA = {};
            instA.dwSize = sizeof(DIDEVICEINSTANCEA);
            using GetInfoA_t = HRESULT(STDMETHODCALLTYPE*)(IDirectInputDevice8*, DIDEVICEINSTANCEA*);
            if (SUCCEEDED((reinterpret_cast<GetInfoA_t>(pGetDeviceInfo))(dev, &instA))) {
                return GET_DIDEVICE_TYPE(instA.dwDevType);
            }

            return 0;
        }

        template <int Slot>
        HRESULT STDMETHODCALLTYPE HookedGetDeviceState(
            IDirectInputDevice8* dev, DWORD cbData, LPVOID lpvData)
        {
            const HRESULT hr = g_origState[Slot](dev, cbData, lpvData);

            if (IsVisible() && SUCCEEDED(hr) && lpvData && cbData)
            {
                if (cbData == 256 || cbData == 16 || cbData == 20) {
                    memset(lpvData, 0, cbData);
                }
                else {
                    DWORD devType = GetDeviceTypeSafe(dev);
                    if (devType == DI8DEVTYPE_KEYBOARD || devType == DI8DEVTYPE_MOUSE) {
                        memset(lpvData, 0, cbData);
                    }
                }
            }
            return hr;
        }

        template <int Slot>
        HRESULT STDMETHODCALLTYPE HookedGetDeviceData(
            IDirectInputDevice8* dev, DWORD cbObjData, LPDIDEVICEOBJECTDATA rgdod,
            LPDWORD pdwInOut, DWORD dwFlags)
        {
            const HRESULT hr = g_origData[Slot](dev, cbObjData, rgdod, pdwInOut, dwFlags);

            if (IsVisible() && pdwInOut && *pdwInOut > 0)
            {
                DWORD devType = GetDeviceTypeSafe(dev);
                if (devType == DI8DEVTYPE_KEYBOARD || devType == DI8DEVTYPE_MOUSE) {
                    *pdwInOut = 0;
                }
            }
            return hr;
        }

        bool GetDeviceMethods(IDirectInput8* di, REFGUID guid, void** outState, void** outData)
        {
            IDirectInputDevice8* dev = nullptr;
            HRESULT hr = di->CreateDevice(guid, &dev, nullptr);
            if (FAILED(hr) || !dev) return false;

            void** vtbl = *reinterpret_cast<void***>(dev);
            *outState = vtbl[kVTableIndex_GetDeviceState];
            *outData = vtbl[kVTableIndex_GetDeviceData];
            dev->Release();
            return true;
        }

        bool HookSlot(int slot, void* pState, void* pData)
        {
            bool ok = false;
            if (slot == 0) {
                ok = MH_CreateHook(pState, &HookedGetDeviceState<0>, reinterpret_cast<void**>(&g_origState[0])) == MH_OK &&
                    MH_CreateHook(pData, &HookedGetDeviceData<0>, reinterpret_cast<void**>(&g_origData[0])) == MH_OK;
            }
            else {
                ok = MH_CreateHook(pState, &HookedGetDeviceState<1>, reinterpret_cast<void**>(&g_origState[1])) == MH_OK &&
                    MH_CreateHook(pData, &HookedGetDeviceData<1>, reinterpret_cast<void**>(&g_origData[1])) == MH_OK;
            }
            return ok;
        }

    }

    bool DInputHook::Initialize()
    {
        HMODULE hMod = GetModuleHandleW(L"dinput8.dll");
        if (!hMod) hMod = LoadLibraryW(L"dinput8.dll");
        if (!hMod) return false;

        using DI8Create_t = HRESULT(WINAPI*)(HINSTANCE, DWORD, REFIID, LPVOID*, LPUNKNOWN);
        auto pCreate = reinterpret_cast<DI8Create_t>(GetProcAddress(hMod, "DirectInput8Create"));
        if (!pCreate) return false;

        IDirectInput8* di = nullptr;
        HRESULT hr = pCreate(GetModuleHandleW(nullptr), DIRECTINPUT_VERSION, IID_IDirectInput8, reinterpret_cast<void**>(&di), nullptr);
        if (FAILED(hr) || !di) return false;

        void* mState = nullptr; void* mData = nullptr;
        void* kState = nullptr; void* kData = nullptr;
        const bool haveMouse = GetDeviceMethods(di, GUID_SysMouse, &mState, &mData);
        const bool haveKbd = GetDeviceMethods(di, GUID_SysKeyboard, &kState, &kData);
        di->Release();

        bool any = false;
        if (haveMouse) any |= HookSlot(0, mState, mData);
        if (haveKbd && (!haveMouse || kState != mState)) any |= HookSlot(1, kState, kData);

        if (!any) return false;

        MH_EnableHook(MH_ALL_HOOKS);
        CAST_LOG_INFO("DInputHook installed (mouse={}, keyboard={})", haveMouse, haveKbd);
        return true;
    }
}