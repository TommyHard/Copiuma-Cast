#pragma once
#include "IGraphicsBackend.h"
#include "D3D11Hook.h"

namespace cast::overlay {

    class D3D11Backend : public IGraphicsBackend {
    public:
        bool Initialize() override {
            return cast::D3D11Hook::Initialize();
        }

        void Shutdown() override {
            cast::D3D11Hook::Shutdown();
        }
    };
}