// OverlayApp.h — CefApp хост-процесса.
// Принудительно включает software-композитинг, чтобы кадры приходили в
// CefRenderHandler::OnPaint (CPU-буфер BGRA), а не в OnAcceleratedPaint
#pragma once

#include "include/cef_app.h"

namespace cast {

    class OverlayApp : public CefApp,
        public CefBrowserProcessHandler {
    public:
        OverlayApp() = default;

        // CefApp
        CefRefPtr<CefBrowserProcessHandler> GetBrowserProcessHandler() override { return this; }
        void OnBeforeCommandLineProcessing(const CefString& process_type,
            CefRefPtr<CefCommandLine> command_line) override;

    private:
        IMPLEMENT_REFCOUNTING(OverlayApp);
        DISALLOW_COPY_AND_ASSIGN(OverlayApp);
    };
}