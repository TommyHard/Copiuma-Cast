using Cast.Desktop.Models;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace Cast.Desktop.Services;

/// <summary>
/// Установка, удаление и проверка целостности модов. Гранулярно:
/// только библиотеки, только мод или всё вместе.
/// Файлы скачиваются с серверов платформы, состояние хранится локально
/// </summary>
public sealed class ModManagerService
{
    private readonly CastApiClient _api;
    private readonly string _statePath;
    private InstalledModState _state;

    public ModManagerService(CastApiClient api)
    {
        _api = api;
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Copiuma.Cast");
        Directory.CreateDirectory(appData);
        _statePath = Path.Combine(appData, "installed_mods.json");
        _state = LoadState();
    }

    public InstalledMod? GetInstalled(string gameId)
        => _state.Mods.GetValueOrDefault(gameId);

    /// <summary>
    /// Установить мод: mode = All | LibrariesOnly | ModOnly
    /// </summary>
    public async Task InstallAsync(ModPackageInfo package, string gameDir,
        InstallMode mode, IProgress<(int current, int total, string file)>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(package.ArchiveUrl))
            throw new InvalidOperationException("Для мода не задан архив (ArchiveUrl).");

        // Скачиваем .zip-архив мода целиком
        using var http = new HttpClient();
        var archiveBytes = await http.GetByteArrayAsync(package.ArchiveUrl, ct).ConfigureAwait(false);

        using var zip = new ZipArchive(new MemoryStream(archiveBytes), ZipArchiveMode.Read);

        // Контрольные суммы из манифеста и выбор файлов под режим установки.
        // allowed == null означает "весь архив" (режим All или манифест без списка)
        var expectedHashes = BuildHashMap(package);
        var allowed = SelectAllowed(package, mode);

        var entries = zip.Entries
            .Where(e => !string.IsNullOrEmpty(e.Name)) // пропускаем каталоги
            .Where(e => allowed is null || allowed.Contains(Normalize(e.FullName)))
            .ToList();

        var installed = new List<InstalledFile>();
        for (var i = 0; i < entries.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var entry = entries[i];
            var rel = entry.FullName;
            progress?.Report((i + 1, entries.Count, rel));

            var targetPath = Path.Combine(gameDir, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

            byte[] bytes;
            await using (var es = entry.Open())
            using (var ms = new MemoryStream())
            {
                await es.CopyToAsync(ms, ct).ConfigureAwait(false);
                bytes = ms.ToArray();
            }

            var hash = ComputeSha256(bytes);
            if (expectedHashes.TryGetValue(Normalize(rel), out var expected)
                && !string.IsNullOrEmpty(expected)
                && !string.Equals(hash, expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Контрольная сумма не совпала для {rel}: ожидалось {expected}, получено {hash}.");
            }

            await File.WriteAllBytesAsync(targetPath, bytes, ct).ConfigureAwait(false);
            installed.Add(new InstalledFile { RelativePath = rel, Sha256 = hash });
        }

        if (_state.Mods.TryGetValue(package.GameId, out var existing))
        {
            var byPath = existing.Files.ToDictionary(f => f.RelativePath, StringComparer.OrdinalIgnoreCase);
            foreach (var f in installed)
                byPath[f.RelativePath] = f;
            existing.Files = byPath.Values.ToList();
            existing.ModVersion = package.ModVersion;
            existing.GameDirectory = gameDir;
        }
        else
        {
            _state.Mods[package.GameId] = new InstalledMod
            {
                GameId = package.GameId,
                GameDirectory = gameDir,
                ModVersion = package.ModVersion,
                InstalledAt = DateTime.UtcNow,
                Files = installed
            };
        }

        SaveState();
    }

    /// <summary>
    /// Удалить все файлы мода и убрать из состояния
    /// </summary>
    public Task UninstallAsync(string gameId)
    {
        if (!_state.Mods.TryGetValue(gameId, out var mod))
            return Task.CompletedTask;

        foreach (var file in mod.Files)
        {
            var path = Path.Combine(mod.GameDirectory, file.RelativePath);
            if (File.Exists(path))
                File.Delete(path);
        }

        _state.Mods.Remove(gameId);
        SaveState();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Проверить целостность: пересчитать SHA-256 каждого файла и сравнить
    /// </summary>
    public async Task<IntegrityReport> VerifyAsync(string gameId, CancellationToken ct = default)
    {
        if (!_state.Mods.TryGetValue(gameId, out var mod))
            return new IntegrityReport(false, new List<string> { "Мод не установлен." });

        var issues = new List<string>();
        foreach (var file in mod.Files)
        {
            ct.ThrowIfCancellationRequested();
            var path = Path.Combine(mod.GameDirectory, file.RelativePath);
            if (!File.Exists(path))
            {
                issues.Add($"Отсутствует: {file.RelativePath}");
                continue;
            }

            var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            var hash = ComputeSha256(bytes);
            if (!string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase))
                issues.Add($"Изменён: {file.RelativePath}");
        }

        return new IntegrityReport(issues.Count == 0, issues);
    }

    private static string Normalize(string path)
        => path.Replace('\\', '/').Trim('/').ToLowerInvariant();

    /// <summary>
    /// relativePath -> ожидаемый sha256 (если задан в манифесте установки)
    /// </summary>
    private static Dictionary<string, string> BuildHashMap(ModPackageInfo package)
    {
        var map = new Dictionary<string, string>();
        void Add(ModFileEntry? f)
        {
            if (f is not null && !string.IsNullOrWhiteSpace(f.RelativePath))
                map[Normalize(f.RelativePath)] = f.Sha256;
        }
        foreach (var f in package.Libraries) Add(f);
        foreach (var f in package.ModFiles) Add(f);
        Add(package.Manifest);
        return map;
    }

    /// <summary>
    /// Набор относительных путей под режим установки. null — ставить весь архив
    /// (режим All либо манифест без перечня файлов — фильтровать нечем)
    /// </summary>
    private static HashSet<string>? SelectAllowed(ModPackageInfo package, InstallMode mode)
    {
        if (mode == InstallMode.All)
            return null;

        var src = mode == InstallMode.LibrariesOnly ? package.Libraries : package.ModFiles;
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in src)
            if (!string.IsNullOrWhiteSpace(f.RelativePath))
                set.Add(Normalize(f.RelativePath));

        return set.Count == 0 ? null : set;
    }

    private static string ComputeSha256(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private InstalledModState LoadState()
    {
        if (!File.Exists(_statePath))
            return new InstalledModState();
        try
        {
            var json = File.ReadAllText(_statePath);
            return JsonSerializer.Deserialize<InstalledModState>(json) ?? new InstalledModState();
        }
        catch
        {
            return new InstalledModState();
        }
    }

    private void SaveState()
    {
        var json = JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_statePath, json);
    }
}

public enum InstallMode { All, LibrariesOnly, ModOnly }

public sealed record IntegrityReport(bool IsValid, List<string> Issues);