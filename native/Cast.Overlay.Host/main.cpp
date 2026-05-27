// main.cpp — точка входа CEF-хоста оверлея
//
// Один и тот же exe работает и как основной (browser) процесс, и как
// суб-процессы CEF (render/gpu/utility)
//
// Аргументы:
//   --game-pid=<pid>   PID процесса игры (для имени shared memory). Обязателен.
//   --url=<url>        Адрес React-интерфейса. По умолчанию dev-сервер Vite.
//   --width=<px>       Ширина off-screen рендера (по умолчанию 1280).
//   --height=<px>      Высота (по умолчанию 720)

#include <windows.h>
#include <cstdlib>
#include <string>
#include "include/cef_app.h"
#include "include/cef_browser.h"
#include "include/cef_command_line.h"
#include "OverlayApp.h"
#include "OverlayClient.h"

int APIENTRY wWinMain(HINSTANCE hInstance, HINSTANCE, LPWSTR, int)
{
    CefMainArgs main_args(hInstance);
    CefRefPtr<cast::OverlayApp> app(new cast::OverlayApp());

    // Если это суб-процесс CEF — отрабатываем и выходим
    const int exit_code = CefExecuteProcess(main_args, app, nullptr);
    if (exit_code >= 0)
        return exit_code;

    // --- основной процесс ---
    CefRefPtr<CefCommandLine> cmd = CefCommandLine::CreateCommandLine();
    cmd->InitFromString(::GetCommandLineW());

    uint32_t gamePid = 0;
    if (cmd->HasSwitch("game-pid"))
        gamePid = static_cast<uint32_t>(_wtoi(cmd->GetSwitchValue("game-pid").ToWString().c_str()));

    std::string url = "http://localhost:5173"; // dev-сервер overlay-ui по умолчанию
    if (cmd->HasSwitch("url"))
        url = cmd->GetSwitchValue("url").ToString();

    int width = cmd->HasSwitch("width") ? atoi(cmd->GetSwitchValue("width").ToString().c_str()) : 1280;
    int height = cmd->HasSwitch("height") ? atoi(cmd->GetSwitchValue("height").ToString().c_str()) : 720;

    CefSettings settings;
    settings.windowless_rendering_enabled = true;
    settings.no_sandbox = true;

    CefInitialize(main_args, settings, app, nullptr);

    CefRefPtr<cast::OverlayClient> client(new cast::OverlayClient(gamePid, width, height));

    CefWindowInfo window_info;
    window_info.SetAsWindowless(nullptr); // off-screen, без физического окна

    CefBrowserSettings browser_settings;
    browser_settings.windowless_frame_rate = 60;

    CefBrowserHost::CreateBrowser(window_info, client, url, browser_settings, nullptr, nullptr);

    CefRunMessageLoop(); // блокируется до закрытия браузера / QuitMessageLoop
    CefShutdown();
    return 0;
}