#include "OverlayApp.h"

namespace cast {

    void OverlayApp::OnBeforeCommandLineProcessing(const CefString& process_type,
        CefRefPtr<CefCommandLine> command_line)
    {
        if (process_type.empty())
        {
            command_line->AppendSwitch("disable-gpu");
            command_line->AppendSwitch("disable-gpu-compositing");
        }

        // Фиксируем масштаб 1.0 для ВСЕХ процессов (значение читают и browser,
        // и renderer). Без этого при системном DPI 150% CEF рендерит страницу с
        // device scale 1.5 — содержимое выглядит увеличенным и вылезает за
        // кадр. 1 CSS-пиксель == 1 пиксель буфера -> размер кадра совпадает с
        // --width/--height
        command_line->AppendSwitchWithValue("force-device-scale-factor", "1");
        command_line->AppendSwitchWithValue("high-dpi-support", "1");
    }
}