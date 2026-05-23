// test_producer.cpp — крошечный продюсер кадров для проверки канала shared
// memory БЕЗ CEF. Пишет анимированный градиент в ту же память, из которой
// читает DLL оверлея. Если в игре виден движущийся полупрозрачный градиент на
// весь экран — значит весь конвейер (IPC → текстура → отрисовка) работает, и
// CEF останется лишь подставить вместо этого свои кадры.
//
// Сборка (из папки native):
//     cl /EHsc /std:c++17 test_producer.cpp
// Запуск (PID процесса игры можно взять в диспетчере задач):
//     test_producer.exe <game_pid>
#include <windows.h>
#include <cstdint>
#include <cstdio>
#include <cstdlib>

#include "Cast.Overlay/SharedFrame.h"

int main(int argc, char** argv)
{
    if (argc < 2)
    {
        printf("usage: test_producer <game_pid>\n");
        return 1;
    }

    const uint32_t pid = static_cast<uint32_t>(strtoul(argv[1], nullptr, 10));

    char name[128];
    cast::ipc::MakeMappingName(name, sizeof(name), pid);

    HANDLE mapping = CreateFileMappingA(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE,
                                        0, cast::ipc::kSharedSize, name);
    if (!mapping)
    {
        printf("CreateFileMapping failed: %lu\n", GetLastError());
        return 1;
    }

    auto* h = static_cast<cast::ipc::FrameHeader*>(
        MapViewOfFile(mapping, FILE_MAP_ALL_ACCESS, 0, 0, cast::ipc::kSharedSize));
    if (!h)
    {
        printf("MapViewOfFile failed: %lu\n", GetLastError());
        return 1;
    }

    printf("producing frames to \"%s\" — Ctrl+C to stop\n", name);

    const uint32_t W = 1280, H = 720, pitch = W * 4;
    uint8_t* px = cast::ipc::FramePixels(h);
    uint32_t frame = 0;

    for (;;)
    {
        InterlockedIncrement(&h->sequence);    // нечётное: начало записи
        h->magic   = cast::ipc::kFrameMagic;
        h->version = cast::ipc::kFrameVersion;
        h->width   = W;
        h->height  = H;
        h->pitch   = pitch;

        for (uint32_t y = 0; y < H; ++y)
        {
            uint32_t* row = reinterpret_cast<uint32_t*>(px + y * pitch);
            for (uint32_t x = 0; x < W; ++x)
            {
                const uint8_t b = static_cast<uint8_t>(x + frame);
                const uint8_t g = static_cast<uint8_t>(y + frame);
                const uint8_t r = static_cast<uint8_t>((x + y) / 2 + frame);
                const uint8_t a = 180;          // полупрозрачно — видно игру под кадром
                row[x] = (uint32_t(a) << 24) | (uint32_t(r) << 16) |
                         (uint32_t(g) << 8) | uint32_t(b);
            }
        }

        InterlockedIncrement(&h->sequence);    // чётное: запись завершена
        ++frame;
        Sleep(16);                              // ~60 кадров/с
    }
}
