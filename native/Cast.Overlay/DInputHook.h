// DInputHook.h — нейтрализация ввода игры через DirectInput8, пока оверлей открыт
//
// Игры вроде GTA SA читают обзор мышью и клавиатуру напрямую через
// IDirectInputDevice8::GetDeviceState / GetDeviceData, минуя очередь оконных
// сообщений, — поэтому подклассинга WndProc недостаточно, камера продолжает
// двигаться. Мы перехватываем эти два метода (адреса берём из vtable временного
// устройства; vtable общий для всех экземпляров класса, поэтому хук действует
// на все устройства игры) и, пока оверлей видим, обнуляем выдаваемое состояние
#pragma once

namespace cast::overlay {

    class DInputHook {
    public:
        // Создаёт и включает хуки GetDeviceState/GetDeviceData. Безопасно, если
        // игра не использует DirectInput
        static bool Initialize();
        // Снятие хуков выполняет общий MH_DisableHook(MH_ALL_HOOKS) в D3D9Hook::Shutdown
    };

}
