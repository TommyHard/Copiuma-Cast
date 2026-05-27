using Cast.Desktop.Models;
using System.IO;
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
        var files = new List<ModFileEntry>();
        if (mode is InstallMode.All or InstallMode.LibrariesOnly)
            files.AddRange(package.Libraries);
        if (mode is InstallMode.All or InstallMode.ModOnly)
            files.AddRange(package.ModFiles);
        if (mode is InstallMode.All && package.Manifest is not null)
            files.Add(package.Manifest);

        var installed = new List<InstalledFile>();
        using var http = new HttpClient();
        for (var i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var entry = files[i];
            progress?.Report((i + 1, files.Count, entry.RelativePath));

            var targetPath = Path.Combine(gameDir, entry.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

            var bytes = await http.GetByteArrayAsync(entry.DownloadUrl, ct).ConfigureAwait(false);
            var hash = ComputeSha256(bytes);
            if (!string.Equals(hash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Контрольная сумма не совпала для {entry.RelativePath}: ожидалось {entry.Sha256}, получено {hash}.");

            await File.WriteAllBytesAsync(targetPath, bytes, ct).ConfigureAwait(false);
            installed.Add(new InstalledFile { RelativePath = entry.RelativePath, Sha256 = hash });
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