// ControlChannel.h — писатель обратного канала (сторона DLL/игры)
//
// Открывает именованное отображение "Local\\CopiumaCast.Control.<pid>" и
// публикует в него размер игры, флаг видимости оверлея и события ввода.
// Читает это CEF-хост (SharedControlReader)

#pragma once
#include <cstdint>
#include "SharedControl.h"

namespace cast::overlay {

    class ControlChannel {
    public:
        // Создаёт/открывает отображение под текущий процесс (PID игры).
        // Идемпотентен. Возвращает false, если отображение создать не удалось
        static bool Open();
        static void Close();
        static bool IsOpen();

        // Публикует размер бэкбуфера игры (seqlock)
        static void SetGameSize(uint32_t width, uint32_t height);

        // Публикует флаг видимости оверлея
        static void SetOverlayVisible(bool visible);

        // Кладёт событие ввода в кольцо. Если кольцо переполнено — событие
        // отбрасывается
        static void PushInput(const ipc::InputEvent& ev);
    };
}