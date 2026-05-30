using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Cast.Desktop.Services;

/// <summary>
/// Запуск игры с захватом правильного PID (отсеивая лаунчеры), 
/// автовыбор архитектуры (x86/x64), инъекция DLL оверлея и старт CEF-хоста
/// </summary>
public sealed class GameLauncher
{
    private readonly DesktopOptions _options;
    public event Action<string>? Log;

    public GameLauncher(DesktopOptions options) => _options = options;

    public Process? Game { get; private set; }
    public Process? OverlayHost { get; private set; }

    public async Task LaunchAsync(string gameExePath, CancellationToken ct = default)
    {
        if (!File.Exists(gameExePath))
            throw new FileNotFoundException("Исполняемый файл игры не найден.", gameExePath);

        string exeName = Path.GetFileNameWithoutExtension(gameExePath);

        Process.Start(new ProcessStartInfo(gameExePath)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(gameExePath)!,
        });

        Log?.Invoke($"Запуск {exeName}. Ожидание появления основного окна игры...");

        // Ищем процесс с активным окном (игнорируем фоновые CEF-процессы и скрытые лаунчеры)
        int maxRetries = 60;
        Process? targetProcess = null;

        for (int i = 0; i < maxRetries && !ct.IsCancellationRequested; i++)
        {
            var processes = Process.GetProcessesByName(exeName);
            // Если есть окно, значит это финальный процесс для отрисовки
            targetProcess = processes.FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);

            if (targetProcess != null)
                break;

            await Task.Delay(1000, ct).ConfigureAwait(false);
        }

        if (targetProcess == null)
            throw new InvalidOperationException($"Не удалось дождаться процесса {exeName} с активным графическим окном.");

        Game = targetProcess;
        Log?.Invoke($"Найден целевой процесс (PID={Game.Id}). Ожидание готовности ввода...");

        // Даём процессу инициализироваться перед инъекцией
        try { Game.WaitForInputIdle(5000); } catch { /* ignore */ }
        await Task.Delay(1500, ct).ConfigureAwait(false);

        // Автоопределение разрядности и выбор нужной DLL
        bool is64Bit = Is64BitProcess(Game);
        string dllName = is64Bit ? "cast.overlay_x64.dll" : "cast.overlay_x86.dll";

        // Формируем путь к DLL
        string basePath = Path.GetDirectoryName(Path.GetFullPath(_options.OverlayDllPath)) ?? AppDomain.CurrentDomain.BaseDirectory;
        string dllPath = Path.Combine(basePath, dllName);

        if (File.Exists(dllPath))
        {
            // Кросс-арх инъекция: адрес LoadLibraryA вычисляется в ЦЕЛЕВОМ процессе
            Inject(Game.Id, dllPath, is64Bit);
            Log?.Invoke($"DLL оверлея [{dllName}] загружена в процесс игры.");
        }
        else
        {
            Log?.Invoke($"DLL оверлея не найдена по пути: {dllPath} (инъекция пропущена).");
        }

        // Запуск CEF-хоста
        if (File.Exists(_options.OverlayHostPath))
        {
            OverlayHost = Process.Start(new ProcessStartInfo(_options.OverlayHostPath,
                $"--game-pid={Game.Id} --url={_options.OverlayUiUrl}")
            { UseShellExecute = false });
            Log?.Invoke("CEF-хост оверлея запущен.");
        }
        else
        {
            Log?.Invoke($"CEF-хост не найден: {_options.OverlayHostPath} (оверлей не поднят).");
        }
    }

    private static bool Is64BitProcess(Process process)
    {
        if (!Environment.Is64BitOperatingSystem)
            return false;

        if (!IsWow64Process(process.Handle, out bool isWow64))
            return Environment.Is64BitOperatingSystem; // Если нет прав на проверку, доверяем ОС

        return !isWow64;
    }

    private static void Inject(int pid, string dllPath, bool target64)
    {
        const uint PROCESS_ALL_ACCESS = 0x1F0FFF;
        const uint MEM_COMMIT_RESERVE = 0x3000;
        const uint PAGE_READWRITE = 0x04;

        var hProcess = OpenProcess(PROCESS_ALL_ACCESS, false, pid);
        if (hProcess == IntPtr.Zero)
            throw new InvalidOperationException("OpenProcess не удался (проверьте права доступа).");

        try
        {
            var bytes = Encoding.ASCII.GetBytes(dllPath + "\0");
            var alloc = VirtualAllocEx(hProcess, IntPtr.Zero, (uint)bytes.Length, MEM_COMMIT_RESERVE, PAGE_READWRITE);
            if (alloc == IntPtr.Zero)
                throw new InvalidOperationException("VirtualAllocEx не удался.");

            if (!WriteProcessMemory(hProcess, alloc, bytes, (uint)bytes.Length, out _))
                throw new InvalidOperationException("WriteProcessMemory не удался.");

            var loadLibrary = GetRemoteLoadLibraryA(pid, target64);
            if (loadLibrary == IntPtr.Zero)
                throw new InvalidOperationException("Не удалось вычислить адрес LoadLibraryA в процессе игры.");

            var thread = CreateRemoteThread(hProcess, IntPtr.Zero, 0, loadLibrary, alloc, 0, out _);
            if (thread == IntPtr.Zero)
                throw new InvalidOperationException("CreateRemoteThread не удался.");

            WaitForSingleObject(thread, 5000);

            // Код завершения потока == результат LoadLibraryA (HMODULE). 0 означает,
            // что DLL НЕ загрузилась (нет зависимостей — напр. несовпадение разрядности)
            GetExitCodeThread(thread, out uint loadResult);
            CloseHandle(thread);

            if (loadResult == 0)
                throw new InvalidOperationException(
                    "LoadLibrary в процессе игры вернул NULL: DLL не загрузилась. " +
                    "Вероятно, она собрана в Debug (нужны MSVCP140D/VCRUNTIME140D) " +
                    "или не совпадает разрядность. Соберите Cast.Overlay в Release.");
        }
        finally
        {
            CloseHandle(hProcess);
        }
    }

    // Адрес LoadLibraryA в адресном пространстве целевого процесса. База kernel32
    // берётся из самого процесса (нужной разрядности), RVA экспорта — из
    // соответствующего kernel32 на диске
    private static IntPtr GetRemoteLoadLibraryA(int pid, bool target64)
    {
        ulong baseAddr = GetRemoteModuleBase(pid, "kernel32.dll");
        if (baseAddr == 0)
            return IntPtr.Zero;

        string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string kernelPath = Path.Combine(winDir, target64 ? "System32" : "SysWOW64", "kernel32.dll");

        uint rva = GetExportRva(kernelPath, "LoadLibraryA");
        if (rva == 0)
            return IntPtr.Zero;

        return (IntPtr)(baseAddr + rva);
    }

    // База модуля в целевом процессе. TH32CS_SNAPMODULE32 нужен, чтобы x64-процесс
    // видел 32-битные модули WOW64-цели.
    private static ulong GetRemoteModuleBase(int pid, string moduleName)
    {
        const uint TH32CS_SNAPMODULE = 0x8, TH32CS_SNAPMODULE32 = 0x10;
        IntPtr snap = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, (uint)pid);
        if (snap == IntPtr.Zero || snap == new IntPtr(-1))
            return 0;
        try
        {
            var me = new MODULEENTRY32 { dwSize = (uint)Marshal.SizeOf<MODULEENTRY32>() };
            if (Module32First(snap, ref me))
            {
                do
                {
                    if (string.Equals(me.szModule, moduleName, StringComparison.OrdinalIgnoreCase))
                        return (ulong)me.modBaseAddr.ToInt64();
                }
                while (Module32Next(snap, ref me));
            }
        }
        finally { CloseHandle(snap); }
        return 0;
    }

    // RVA экспортируемой функции из PE-файла на диске (разбор таблицы экспорта)
    private static uint GetExportRva(string pePath, string exportName)
    {
        byte[] f = File.ReadAllBytes(pePath);
        int peOff = BitConverter.ToInt32(f, 0x3C);
        int coff = peOff + 4;                               // IMAGE_FILE_HEADER
        ushort numSections = BitConverter.ToUInt16(f, coff + 2);
        ushort optSize = BitConverter.ToUInt16(f, coff + 16);
        int opt = coff + 20;                                // IMAGE_OPTIONAL_HEADER
        bool pe32plus = BitConverter.ToUInt16(f, opt) == 0x20b;
        int dataDir = opt + (pe32plus ? 112 : 96);          // DataDirectory[0] = Export
        uint exportRva = BitConverter.ToUInt32(f, dataDir);
        int sections = opt + optSize;

        int Rva2Off(uint rva)
        {
            for (int i = 0; i < numSections; i++)
            {
                int s = sections + i * 40;
                uint va = BitConverter.ToUInt32(f, s + 12);
                uint vsize = BitConverter.ToUInt32(f, s + 8);
                uint raw = BitConverter.ToUInt32(f, s + 20);
                if (rva >= va && rva < va + vsize)
                    return (int)(raw + (rva - va));
            }
            return -1;
        }

        int exp = Rva2Off(exportRva);
        if (exp < 0) return 0;

        uint numNames = BitConverter.ToUInt32(f, exp + 24);
        int eat = Rva2Off(BitConverter.ToUInt32(f, exp + 28));    // AddressOfFunctions
        int names = Rva2Off(BitConverter.ToUInt32(f, exp + 32));  // AddressOfNames
        int ords = Rva2Off(BitConverter.ToUInt32(f, exp + 36));   // AddressOfNameOrdinals
        if (eat < 0 || names < 0 || ords < 0) return 0;

        for (uint i = 0; i < numNames; i++)
        {
            int nameOff = Rva2Off(BitConverter.ToUInt32(f, names + (int)(i * 4)));
            if (nameOff < 0) continue;
            int end = nameOff;
            while (f[end] != 0) end++;
            if (Encoding.ASCII.GetString(f, nameOff, end - nameOff) == exportName)
            {
                ushort ord = BitConverter.ToUInt16(f, ords + (int)(i * 2));
                return BitConverter.ToUInt32(f, eat + ord * 4);
            }
        }
        return 0;
    }

    // --- WinAPI ---
    [DllImport("kernel32.dll", SetLastError = true, CallingConvention = CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWow64Process([In] IntPtr process, [Out] out bool wow64Process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr addr, uint size, uint type, uint protect);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr addr, byte[] buffer, uint size, out IntPtr written);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr attrs, uint stack, IntPtr start, IntPtr param, uint flags, out IntPtr threadId);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern bool Module32First(IntPtr snapshot, ref MODULEENTRY32 me);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern bool Module32Next(IntPtr snapshot, ref MODULEENTRY32 me);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct MODULEENTRY32
    {
        public uint dwSize;
        public uint th32ModuleID;
        public uint th32ProcessID;
        public uint GlblcntUsage;
        public uint ProccntUsage;
        public IntPtr modBaseAddr;
        public uint modBaseSize;
        public IntPtr hModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExePath;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint ms);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeThread(IntPtr handle, out uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}