#include "pch.h"
#include "SharedFrameReader.h"
#include "Logger.h"

#include <cstring>

namespace cast {

    bool SharedFrameReader::Open(uint32_t gamePid)
    {
        char name[128];
        ipc::MakeMappingName(name, sizeof(name), gamePid);

        // CreateFileMapping создаёт отображение или открывает уже существующее —
        // неважно, кто стартовал первым (DLL или хост)
        m_mapping = CreateFileMappingA(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE,
            0, ipc::kSharedSize, name);
        if (!m_mapping)
        {
            CAST_LOG_ERROR("SharedFrameReader: CreateFileMapping failed: {}", GetLastError());
            return false;
        }

        m_view = static_cast<ipc::FrameHeader*>(
            MapViewOfFile(m_mapping, FILE_MAP_ALL_ACCESS, 0, 0, ipc::kSharedSize));
        if (!m_view)
        {
            CAST_LOG_ERROR("SharedFrameReader: MapViewOfFile failed: {}", GetLastError());
            CloseHandle(m_mapping);
            m_mapping = nullptr;
            return false;
        }

        CAST_LOG_INFO("SharedFrameReader ready: {}", name);
        return true;
    }

    void SharedFrameReader::Close()
    {
        if (m_view) { UnmapViewOfFile(m_view); m_view = nullptr; }
        if (m_mapping) { CloseHandle(m_mapping);  m_mapping = nullptr; }
    }

    bool SharedFrameReader::HasProducer() const
    {
        return m_view && m_view->magic == ipc::kFrameMagic && m_view->width > 0;
    }

    LONG SharedFrameReader::Sequence() const
    {
        return m_view ? m_view->sequence : 0;
    }

    bool SharedFrameReader::PeekSize(uint32_t& width, uint32_t& height) const
    {
        if (!m_view)
            return false;

        const LONG s1 = m_view->sequence;
        MemoryBarrier();
        if (s1 & 1) return false;
        if (m_view->magic != ipc::kFrameMagic) return false;

        const uint32_t w = m_view->width;
        const uint32_t h = m_view->height;
        MemoryBarrier();
        if (m_view->sequence != s1) return false;

        if (!w || !h || w > ipc::kMaxWidth || h > ipc::kMaxHeight)
            return false;

        width = w;
        height = h;
        return true;
    }

    bool SharedFrameReader::ReadInto(uint8_t* dst, uint32_t dstPitch,
        uint32_t& outWidth, uint32_t& outHeight)
    {
        if (!m_view || !dst)
            return false;

        const LONG s1 = m_view->sequence;
        MemoryBarrier();
        if (s1 & 1) return false;
        if (m_view->magic != ipc::kFrameMagic) return false;

        const uint32_t w = m_view->width;
        const uint32_t h = m_view->height;
        const uint32_t pitch = m_view->pitch;
        if (!w || !h || w > ipc::kMaxWidth || h > ipc::kMaxHeight)
            return false;

        const uint8_t* src = ipc::FramePixels(m_view);
        const uint32_t copyLen = (pitch < dstPitch) ? pitch : dstPitch;
        for (uint32_t y = 0; y < h; ++y)
            memcpy(dst + size_t(y) * dstPitch, src + size_t(y) * pitch, copyLen);

        MemoryBarrier();
        if (m_view->sequence != s1)
            return false;

        outWidth = w;
        outHeight = h;
        return true;
    }

}