// SharedFrameWriter.h — продюсерская сторона канала кадров (сторона хоста).
// Зеркало SharedFrameReader из DLL. Не зависит от CEF: принимает готовый
// BGRA-буфер и публикует его в shared memory по протоколу seqlock из
// SharedFrame.h. Сюда CEF будет отдавать кадры из OnPaint
#pragma once

#include <windows.h>
#include <cstdint>
#include "../Cast.Overlay/SharedFrame.h"

namespace cast {

    class SharedFrameWriter {
    public:
        bool Open(uint32_t gamePid);
        void Close();
        bool IsOpen() const { return m_view != nullptr; }

        void Write(const void* bgra, uint32_t width, uint32_t height, uint32_t srcPitch);

    private:
        HANDLE            m_mapping = nullptr;
        ipc::FrameHeader* m_view = nullptr;
    };
}