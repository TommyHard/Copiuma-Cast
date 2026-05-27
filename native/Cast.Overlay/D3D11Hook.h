#pragma once

namespace cast {
    class D3D11Hook {
    public:
        static bool Initialize();
        static void Shutdown();
    };
}