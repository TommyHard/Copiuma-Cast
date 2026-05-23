#include "pch.h"
#include "Logger.h"

#include <fstream>
#include <mutex>

namespace cast {
    namespace {

        std::mutex    g_mutex;
        std::ofstream g_file;

        // Путь к лог-файлу: %TEMP%\CopiumaCast\overlay.log
        std::wstring ResolveLogPath()
        {
            wchar_t temp[MAX_PATH] = {};
            GetTempPathW(MAX_PATH, temp);

            std::wstring dir = std::wstring(temp) + L"CopiumaCast";
            CreateDirectoryW(dir.c_str(), nullptr);

            return dir + L"\\overlay.log";
        }

    }

    void Logger::Init()
    {
        std::lock_guard<std::mutex> lock(g_mutex);
        if (g_file.is_open())
            return;

        g_file.open(ResolveLogPath(), std::ios::out | std::ios::app);
        if (g_file.is_open())
            g_file << "\n===== Cast.Overlay attached (pid=" << GetCurrentProcessId() << ") =====\n";
        g_file.flush();
    }

    void Logger::Shutdown()
    {
        std::lock_guard<std::mutex> lock(g_mutex);
        if (g_file.is_open())
        {
            g_file << "===== Cast.Overlay detached =====\n";
            g_file.close();
        }
    }

    void Logger::Write(const char* level, const std::string& message)
    {
        std::lock_guard<std::mutex> lock(g_mutex);
        if (!g_file.is_open())
            return;

        SYSTEMTIME st;
        GetLocalTime(&st);

        char ts[32];
        sprintf_s(ts, "%02d:%02d:%02d.%03d",
            st.wHour, st.wMinute, st.wSecond, st.wMilliseconds);

        g_file << '[' << ts << "][" << level << "] " << message << '\n';
        g_file.flush();
    }

}