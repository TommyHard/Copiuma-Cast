// dllmain.cpp : точка входа DLL оверлея Cast.Overlay
//
// внутри DllMain удерживается loader lock, поэтому здесь
// НЕЛЬЗЯ делать ничего тяжёлого. Вся инициализация выносится в отдельный
// поток
#include "pch.h"
#include "Overlay.h"

#include <thread>

namespace {

    DWORD WINAPI InitThread(LPVOID /*param*/)
    {
        cast::overlay::Initialize();
        return 0;
    }

}

BOOL APIENTRY DllMain(HMODULE hModule,
    DWORD   ul_reason_for_call,
    LPVOID  /*lpReserved*/)
{
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
        DisableThreadLibraryCalls(hModule);
        if (HANDLE h = CreateThread(nullptr, 0, InitThread, nullptr, 0, nullptr))
            CloseHandle(h);
        break;

    case DLL_PROCESS_DETACH:
        cast::overlay::Shutdown();
        break;
    }
    return TRUE;
}