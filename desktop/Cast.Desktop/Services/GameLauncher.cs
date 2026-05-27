using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Cast.Desktop.Services;

/// <summary>
/// Запуск игры с захватом PID, инъекция DLL оверлея (Cast.Overlay) методом
/// LoadLibrary и старт CEF-хоста оверлея (Cast.Overlay.Host) с PID игры
/// </summary>
public sealed class GameLauncher
{
    private readonly DesktopOptions _options;
    public event Action<string>? Log;

    public GameLauncher(DesktopOptions options) => _options = options;

    public Process? Game { get; private set; }
    public Process? OverlayHost { get; private set; }

    /// <summary>
    /// Запускает игру, инжектирует DLL оверлея и поднимает CEF-хост
    /// </summary>
    public async Task LaunchAsync(string gameExePath, CancellationToken ct = default)
    {
        if (!File.Exists(gameExePath))
            throw new FileNotFoundException("Исполняемый файл игры не найден.", gameExePath);

        Game = Process.Start(new ProcessStartInfo(gameExePath)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(gameExePath)!,
        }) ?? throw new InvalidOperationException("Не удалось запустить игру.");

        Log?.Invoke($"Игра запущена, PID={Game.Id}.");

        // Даём процессу инициализироваться перед инъекцией
        try { Game.WaitForInputIdle(5000); } catch { /* ignore */ }
        await Task.Delay(1500, ct).ConfigureAwait(false);

        var dll = Path.GetFullPath(_options.OverlayDllPath);
        if (File.Exists(dll))
        {
            Inject(Game.Id, dll);
            Log?.Invoke("DLL оверлея инжектирована.");
        }
        else
        {
            Log?.Invoke($"DLL оверлея не найдена: {dll} (инъекция пропущена).");
        }

        // CEF-хост рендерит React-оверлей off-screen и кладёт кадры в shared memory
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

    /// <summary>
    /// Инъекция: пишем путь к DLL в память процесса и вызываем там
    /// LoadLibraryA через CreateRemoteThread
    /// </summary>
    private static void Inject(int pid, string dllPath)
    {
        const uint PROCESS_ALL_ACCESS = 0x1F0FFF;
        const uint MEM_COMMIT_RESERVE = 0x3000;
        const uint PAGE_READWRITE = 0x04;

        var hProcess = OpenProcess(PROCESS_ALL_ACCESS, false, pid);
        if (hProcess == IntPtr.Zero)
            throw new InvalidOperationException("OpenProcess не удался (нужны права/совпадение разрядности).");

        try
        {
            var bytes = Encoding.ASCII.GetBytes(dllPath + "\0");
            var alloc = VirtualAllocEx(hProcess, IntPtr.Zero, (uint)bytes.Length, MEM_COMMIT_RESERVE, PAGE_READWRITE);
            if (alloc == IntPtr.Zero)
                throw new InvalidOperationException("VirtualAllocEx не удался.");

            if (!WriteProcessMemory(hProcess, alloc, bytes, (uint)bytes.Length, out _))
                throw new InvalidOperationException("WriteProcessMemory не удался.");

            var kernel32 = GetModuleHandle("kernel32.dll");
            var loadLibrary = GetProcAddress(kernel32, "LoadLibraryA");
            if (loadLibrary == IntPtr.Zero)
                throw new InvalidOperationException("Не найден LoadLibraryA.");

            var thread = CreateRemoteThread(hProcess, IntPtr.Zero, 0, loadLibrary, alloc, 0, out _);
            if (thread == IntPtr.Zero)
                throw new InvalidOperationException("CreateRemoteThread не удался (проверьте разрядность).");

            WaitForSingleObject(thread, 5000);
            CloseHandle(thread);
        }
        finally
        {
            CloseHandle(hProcess);
        }
    }

    // --- WinAPI ---
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr addr, uint size, uint type, uint protect);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr addr, byte[] buffer, uint size, out IntPtr written);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr attrs, uint stack, IntPtr start, IntPtr param, uint flags, out IntPtr threadId);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint ms);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}