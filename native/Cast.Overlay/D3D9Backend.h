#pragma once
#include "IGraphicsBackend.h"
#include "D3D9Hook.h"

namespace cast::overlay {

    class D3D9Backend : public IGraphicsBackend {
    public:
        bool Initialize() override {
            return cast::D3D9Hook::Initialize();
        }

        void Shutdown() override {
            cast::D3D9Hook::Shutdown();
        }
    };
}