// SharedControlReader.h — консьюмер обратного канала (сторона CEF-хоста)
//
// Открывает "Local\\CopiumaCast.Control.<pid>", созданное DLL оверлея, и читает:
//   * размер игры (seqlock-safe);
//   * события ввода из SPSC-кольца (хост — единственный консьюмер)
#pragma once

#include <windows.h>
#include <cstdint>

#include "../Cast.Overlay/SharedControl.h"

namespace cast {

    class SharedControlReader {
    public:
        ~SharedControlReader() { Close(); }

        bool Open(uint32_t gamePid);
        void Close();
        bool IsOpen() const { return m_block != nullptr; }

        // Активна ли DLL-сторона
        bool HasProducer() const;

        // Размер игры через seqlock. Возвращает false при «разорванном» чтении
        // или если размер ещё не опубликован
        bool ReadSize(uint32_t& width, uint32_t& height) const;

        // Виден ли оверлей сейчас (по флагу из DLL)
        bool OverlayVisible() const
        {
            return HasProducer() && m_block->overlayVisible != 0;
        }

        // Достаёт одно событие ввода из кольца. false — если кольцо пусто
        bool PopInput(ipc::InputEvent& out);

    private:
        HANDLE             m_mapping = nullptr;
        ipc::ControlBlock* m_block = nullptr;
    };

}