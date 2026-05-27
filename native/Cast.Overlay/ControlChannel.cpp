#include "pch.h"
#include "ControlChannel.h"
#include "Logger.h"
#include <atomic>

namespace cast::overlay {
    namespace {

        HANDLE             g_mapping = nullptr;
        ipc::ControlBlock* g_block = nullptr;
        std::atomic<bool>  g_open{ false };

    }

    bool ControlChannel::Open()
    {
        if (g_open.load(std::memory_order_acquire))
            return true;

        char name[128];
        ipc::MakeControlName(name, sizeof(name), GetCurrentProcessId());

        g_mapping = CreateFileMappingA(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE,
            0, ipc::kControlSize, name);
        if (!g_mapping)
        {
            CAST_LOG_ERROR("ControlChannel: CreateFileMapping failed: {}", GetLastError());
            return false;
        }

        g_block = reinterpret_cast<ipc::ControlBlock*>(
            MapViewOfFile(g_mapping, FILE_MAP_ALL_ACCESS, 0, 0, ipc::kControlSize));
        if (!g_block)
        {
            CAST_LOG_ERROR("ControlChannel: MapViewOfFile failed: {}", GetLastError());
            CloseHandle(g_mapping);
            g_mapping = nullptr;
            return false;
        }

        // Инициализируем заголовок. Кольцо/счётчики обнуляем — единственный,
        // кто создаёт это отображение (хост только читает)
        g_block->version = ipc::kControlVersion;
        g_block->sizeSeq = 0;
        g_block->gameWidth = 0;
        g_block->gameHeight = 0;
        g_block->overlayVisible = 0;
        g_block->head = 0;
        g_block->tail = 0;
        MemoryBarrier();
        g_block->magic = ipc::kControlMagic;

        g_open.store(true, std::memory_order_release);
        CAST_LOG_INFO("ControlChannel open ({})", name);
        return true;
    }

    void ControlChannel::Close()
    {
        if (!g_open.exchange(false, std::memory_order_acq_rel))
            return;
        if (g_block)
        {
            g_block->magic = 0;
            UnmapViewOfFile(g_block);
            g_block = nullptr;
        }
        if (g_mapping)
        {
            CloseHandle(g_mapping);
            g_mapping = nullptr;
        }
        CAST_LOG_INFO("ControlChannel closed");
    }

    bool ControlChannel::IsOpen()
    {
        return g_open.load(std::memory_order_acquire);
    }

    void ControlChannel::SetGameSize(uint32_t width, uint32_t height)
    {
        if (!g_open.load(std::memory_order_acquire) || !g_block)
            return;
        if (width == 0 || height == 0)
            return;
        if (g_block->gameWidth == width && g_block->gameHeight == height)
            return;

        InterlockedIncrement(&g_block->sizeSeq); // -> нечётное: начали запись
        g_block->gameWidth = width;
        g_block->gameHeight = height;
        MemoryBarrier();
        InterlockedIncrement(&g_block->sizeSeq); // -> чётное: запись завершена

        CAST_LOG_INFO("ControlChannel: game size -> {}x{}", width, height);
    }

    void ControlChannel::SetOverlayVisible(bool visible)
    {
        if (!g_open.load(std::memory_order_acquire) || !g_block)
            return;
        g_block->overlayVisible = visible ? 1u : 0u;
    }

    void ControlChannel::PushInput(const ipc::InputEvent& ev)
    {
        if (!g_open.load(std::memory_order_acquire) || !g_block)
            return;

        const LONG head = g_block->head;
        const LONG tail = g_block->tail; // consumer двигает; читаем как снимок
        // Заполнено, если занято capacity элементов
        if (static_cast<uint32_t>(head - tail) >= ipc::kInputRingCapacity)
            return;

        const uint32_t idx = static_cast<uint32_t>(head) & (ipc::kInputRingCapacity - 1);
        g_block->ring[idx] = ev;
        MemoryBarrier();
        InterlockedIncrement(&g_block->head);
    }

}