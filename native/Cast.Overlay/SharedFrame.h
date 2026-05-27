// SharedFrame.h — контракт IPC между процессом-источником UI (CEF-хост или
// тестовый продюсер) и инжектированной DLL оверлея
//
// Кадр UI рендерится в соседнем процессе и передаётся в игру через именованное
// отображение памяти (shared memory). Синхронизация — seqlock на счётчике
// последовательности: чётное значение = кадр стабилен, нечётное = идёт запись.
// Позволяет читать без блокировок и без "разорванных" кадров
//
// Формат пикселей: BGRA8 (порядок байт B,G,R,A) — совпадает и с D3DFMT_A8R8G8B8
// в памяти, и с тем, что отдаёт CEF в OnPaint, поэтому конвертация не нужна
#pragma once

#include <windows.h>
#include <cstdint>
#include <cstdio>

namespace cast::ipc {

    constexpr uint32_t kFrameMagic = 0x4D524643;
    constexpr uint32_t kFrameVersion = 1;

    // Максимальное разрешение кадра — под него резервируется буфер отображения.
    // Если разрешение игры больше лимита 4K, кадр обрезается,
    // и UI растягивается со сдвигом координат (клики промахиваются)
    constexpr uint32_t kMaxWidth = 3840;
    constexpr uint32_t kMaxHeight = 2160;
    constexpr uint32_t kBytesPerPixel = 4;

#pragma pack(push, 4)
    struct FrameHeader {
        uint32_t     magic;       // kFrameMagic, когда продюсер активен
        uint32_t     version;     // kFrameVersion
        uint32_t     width;       // ширина текущего кадра
        uint32_t     height;      // высота текущего кадра
        uint32_t     pitch;       // байт в строке (>= width * 4)
        volatile LONG sequence;   // seqlock: чётное — стабильно, нечётное — запись
        uint32_t     reserved[2];
    };
#pragma pack(pop)

    // Полный размер именованного отображения
    constexpr uint32_t kSharedSize =
        sizeof(FrameHeader) + kMaxWidth * kMaxHeight * kBytesPerPixel;

    // Имя отображения привязано к PID процесса игры, чтобы хост и DLL находили друг
    // друга: например, "Local\\CopiumaCast.Overlay.6360"
    inline void MakeMappingName(char* out, size_t cap, uint32_t gamePid)
    {
        _snprintf_s(out, cap, _TRUNCATE, "Local\\CopiumaCast.Overlay.%u", gamePid);
    }

    inline uint8_t* FramePixels(FrameHeader* h)
    {
        return reinterpret_cast<uint8_t*>(h) + sizeof(FrameHeader);
    }
}