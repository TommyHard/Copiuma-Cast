#include "SharedControlReader.h"

namespace cast {

    bool SharedControlReader::Open(uint32_t gamePid)
    {
        Close();

        char name[128];
        ipc::MakeControlName(name, sizeof(name), gamePid);

        // DLL создаёт отображение; хост открывает существующее. Если DLL ещё не
        // поднялась — OpenFileMapping вернёт null, попробуем позже
        m_mapping = OpenFileMappingA(FILE_MAP_ALL_ACCESS, FALSE, name);
        if (!m_mapping)
            return false;

        m_block = reinterpret_cast<ipc::ControlBlock*>(
            MapViewOfFile(m_mapping, FILE_MAP_ALL_ACCESS, 0, 0, ipc::kControlSize));
        if (!m_block)
        {
            CloseHandle(m_mapping);
            m_mapping = nullptr;
            return false;
        }
        return true;
    }

    void SharedControlReader::Close()
    {
        if (m_block) { UnmapViewOfFile(m_block); m_block = nullptr; }
        if (m_mapping) { CloseHandle(m_mapping); m_mapping = nullptr; }
    }

    bool SharedControlReader::HasProducer() const
    {
        return m_block && m_block->magic == ipc::kControlMagic;
    }

    bool SharedControlReader::ReadSize(uint32_t& width, uint32_t& height) const
    {
        if (!HasProducer())
            return false;

        const LONG s1 = m_block->sizeSeq;
        if (s1 & 1)              // нечётное — идёт запись
            return false;
        MemoryBarrier();
        const uint32_t w = m_block->gameWidth;
        const uint32_t h = m_block->gameHeight;
        MemoryBarrier();
        const LONG s2 = m_block->sizeSeq;
        if (s1 != s2 || w == 0 || h == 0)
            return false;        // запись пересеклась с чтением или размер пуст

        width = w;
        height = h;
        return true;
    }

    bool SharedControlReader::PopInput(ipc::InputEvent& out)
    {
        if (!HasProducer())
            return false;

        const LONG head = m_block->head; // продюсер двигает; снимок
        const LONG tail = m_block->tail;
        if (tail == head)
            return false;        // пусто

        MemoryBarrier();         // элемент опубликован продюсером до head
        const uint32_t idx = static_cast<uint32_t>(tail) & (ipc::kInputRingCapacity - 1);
        out = m_block->ring[idx];
        MemoryBarrier();
        m_block->tail = tail + 1; // публикуем продвижение консьюмера
        return true;
    }

}