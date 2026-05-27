// SharedFrameReader.h — потребитель кадров оверлея (сторона DLL).
// Открывает именованное отображение для текущего процесса игры и читает
// кадры без "разрывов" по протоколу seqlock из SharedFrame.h
#pragma once

#include <windows.h>
#include <cstdint>
#include "SharedFrame.h"

namespace cast {

    class SharedFrameReader {
    public:
        // Открывает (или создаёт, если хост ещё не запущен) отображение для gamePid
        bool Open(uint32_t gamePid);
        void Close();

        // Активен ли продюсер (хост записал валидный заголовок)
        bool HasProducer() const;

        // Текущее значение счётчика (для определения "появился ли новый кадр")
        LONG Sequence() const;

        // Стабильные размеры текущего кадра (до создания текстуры под них)
        bool PeekSize(uint32_t& width, uint32_t& height) const;

        // Tear-free копирование текущего кадра в dst. false — кадр менялся во время
        // чтения (повторить на следующем кадре) или продюсер неактивен
        bool ReadInto(uint8_t* dst, uint32_t dstPitch, uint32_t& outWidth, uint32_t& outHeight);

    private:
        HANDLE            m_mapping = nullptr;
        ipc::FrameHeader* m_view = nullptr;
    };
}