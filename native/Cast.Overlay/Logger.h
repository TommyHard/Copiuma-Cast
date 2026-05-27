// Logger.h — файловый логгер
// Инжектированную DLL невозможно отлаживать вслепую, весь
// жизненный цикл оверлея пишется в %TEMP%\CopiumaCast\overlay.log
#pragma once

#include <string>
#include <format>

namespace cast {

    class Logger {
    public:
        static void Init();
        static void Shutdown();
        static void Write(const char* level, const std::string& message);
    };

}

#define CAST_LOG_INFO(...)  ::cast::Logger::Write("INFO",  std::format(__VA_ARGS__))
#define CAST_LOG_WARN(...)  ::cast::Logger::Write("WARN",  std::format(__VA_ARGS__))
#define CAST_LOG_ERROR(...) ::cast::Logger::Write("ERROR", std::format(__VA_ARGS__))