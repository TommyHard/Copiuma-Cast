// D3D9Hook.h — перехват методов IDirect3DDevice9 игры
//
// Cоздаём D3D9-устройство, читаем из его vtable
// адреса методов EndScene и Reset, ставим на них хуки через
// MinHook. EndScene — точка, где игра завершила кадр: рисуем поверх оверлей.
// Reset — пересоздание устройства: освобождаем/восстанавливаем ресурсы
#pragma once

namespace cast {

    class D3D9Hook {
    public:
        static bool Initialize();
        static void Shutdown();
    };
}