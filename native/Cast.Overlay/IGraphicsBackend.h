#pragma once

namespace cast::overlay {

    class IGraphicsBackend {
    public:
        virtual ~IGraphicsBackend() = default;

        virtual bool Initialize() = 0;

        virtual void Shutdown() = 0;
    };
}