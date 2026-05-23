// SharedControl.h — обратный канал IPC: DLL оверлея (в игре) → CEF-хост.
//
// Прямой канал (SharedFrame.h) несёт кадры UI из хоста в игру. Этот канал идёт
// в обратную сторону и передаёт хосту:
//   * реальный размер бэкбуфера игры — чтобы хост рендерил страницу 1:1,
//     а не в фиксированные 1280x720 с последующим растягиванием;
//   * флаг видимости оверлея;
//   * поток событий ввода (мышь/клавиатура), перехваченных в игре, — хост
//     транслирует их в CEF (SendMouseEvent / SendKeyEvent).
//
// Размер игры защищён seqlock'ом (как кадр). События ввода идут через
// односторонний кольцевой буфер SPSC: единственный продюсер (DLL) двигает head,
// единственный консьюмер (хост) двигает tail — без блокировок.
//
// Структуры одинаковы для x86-DLL и x64-хоста: фиксированная ширина полей,
// volatile LONG (4 байта в обеих разрядностях), #pragma pack(4), без указателей.
#pragma once

#include <windows.h>
#include <cstdint>
#include <cstdio>

namespace cast::ipc {

    constexpr uint32_t kControlMagic = 0x4343524C; // 'CCRL'
    constexpr uint32_t kControlVersion = 1;

    // Ёмкость кольца событий ввода (степень двойки для дешёвого modulo).
    constexpr uint32_t kInputRingCapacity = 256;

    // Тип события ввода.
    enum InputType : uint32_t {
        kInputMouseMove = 1,
        kInputMouseDown = 2,
        kInputMouseUp = 3,
        kInputMouseWheel = 4,
        kInputKeyDown = 5,
        kInputKeyUp = 6,
        kInputChar = 7,
    };

    // Кнопки мыши (для Down/Up).
    enum MouseButton : int32_t {
        kMouseLeft = 0,
        kMouseMiddle = 1,
        kMouseRight = 2,
    };

    // Модификаторы — собственные биты (не зависят от заголовков CEF).
    // Хост транслирует их в EVENTFLAG_* при формировании события.
    enum ModFlags : uint32_t {
        kModShift = 1u << 0,
        kModCtrl = 1u << 1,
        kModAlt = 1u << 2,
        kModLeftBtn = 1u << 3,
        kModMiddleBtn = 1u << 4,
        kModRightBtn = 1u << 5,
    };

#pragma pack(push, 4)
    struct InputEvent {
        uint32_t type;        // InputType
        int32_t  x;           // позиция курсора (пиксели кадра/клиентской области)
        int32_t  y;
        int32_t  button;      // MouseButton — для Down/Up
        int32_t  wheelDelta;  // для MouseWheel (как в WM_MOUSEWHEEL: кратно 120)
        uint32_t key;         // VK-код (Key*) либо код символа (Char)
        uint32_t modifiers;   // комбинация ModFlags
    };

    struct ControlBlock {
        uint32_t magic;            // kControlMagic, когда DLL активна
        uint32_t version;          // kControlVersion

        // --- размер игры (seqlock на sizeSeq) ---
        volatile LONG sizeSeq;     // чётное — стабильно, нечётное — запись
        uint32_t gameWidth;
        uint32_t gameHeight;
        uint32_t overlayVisible;   // 0/1

        // --- кольцо событий ввода (SPSC) ---
        volatile LONG head;        // продюсер (DLL): индекс следующей записи
        volatile LONG tail;        // консьюмер (хост): индекс следующего чтения
        uint32_t reserved[2];
        InputEvent ring[kInputRingCapacity];
    };
#pragma pack(pop)

    constexpr uint32_t kControlSize = sizeof(ControlBlock);

    // Имя отображения привязано к PID игры, например "Local\\CopiumaCast.Control.6360".
    inline void MakeControlName(char* out, size_t cap, uint32_t gamePid)
    {
        _snprintf_s(out, cap, _TRUNCATE, "Local\\CopiumaCast.Control.%u", gamePid);
    }

} // namespace cast::ipc
