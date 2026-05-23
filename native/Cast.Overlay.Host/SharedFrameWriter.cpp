#include "SharedFrameWriter.h"
#include <cstring>

namespace cast {

    bool SharedFrameWriter::Open(uint32_t gamePid)
    {
        char name[128];
        ipc::MakeMappingName(name, sizeof(name), gamePid);

        m_mapping = CreateFileMappingA(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE,
            0, ipc::kSharedSize, name);
        if (!m_mapping)
            return false;

        m_view = static_cast<ipc::FrameHeader*>(
            MapViewOfFile(m_mapping, FILE_MAP_ALL_ACCESS, 0, 0, ipc::kSharedSize));
        if (!m_view)
        {
            CloseHandle(m_mapping);
            m_mapping = nullptr;
            return false;
        }
        return true;
    }

    void SharedFrameWriter::Close()
    {
        if (m_view) { UnmapViewOfFile(m_view); m_view = nullptr; }
        if (m_mapping) { CloseHandle(m_mapping);  m_mapping = nullptr; }
    }

    void SharedFrameWriter::Write(const void* bgra, uint32_t width, uint32_t height,
        uint32_t srcPitch)
    {
        if (!m_view || !bgra)
            return;

        if (width > ipc::kMaxWidth)  width = ipc::kMaxWidth;
        if (height > ipc::kMaxHeight) height = ipc::kMaxHeight;

        const uint32_t dstPitch = width * ipc::kBytesPerPixel;
        const uint32_t copyLen = (srcPitch < dstPitch) ? srcPitch : dstPitch;

        // seqlock: нечётное -> запись -> чётное
        InterlockedIncrement(&m_view->sequence);

        m_view->magic = ipc::kFrameMagic;
        m_view->version = ipc::kFrameVersion;
        m_view->width = width;
        m_view->height = height;
        m_view->pitch = dstPitch;

        uint8_t* dst = ipc::FramePixels(m_view);
        const uint8_t* src = static_cast<const uint8_t*>(bgra);
        for (uint32_t y = 0; y < height; ++y)
            memcpy(dst + size_t(y) * dstPitch, src + size_t(y) * srcPitch, copyLen);

        InterlockedIncrement(&m_view->sequence);
    }

}