using Cast.Shared.GameBridge;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cast.GameBridge;

/// <summary>
/// Загрузка манифеста мода из JSON-файла рядом с модом. Манифест — белый список
/// доступных событий; его читает десктоп и публикует бэкенд
/// </summary>
public static class ManifestLoader
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static async Task<GameManifest> LoadAsync(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Манифест мода не найден: {path}", path);

        await using var fs = File.OpenRead(path);
        var manifest = await JsonSerializer.DeserializeAsync<GameManifest>(fs, JsonOptions, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException($"Манифест пуст или некорректен: {path}");

        Validate(manifest, path);
        return manifest;
    }

    public static GameManifest Parse(string json)
    {
        var manifest = JsonSerializer.Deserialize<GameManifest>(json, JsonOptions)
            ?? throw new InvalidDataException("Манифест пуст или некорректен.");
        Validate(manifest, "<inline>");
        return manifest;
    }

    private static void Validate(GameManifest m, string source)
    {
        if (string.IsNullOrWhiteSpace(m.GameId))
            throw new InvalidDataException($"Манифест {source}: пустой gameId.");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in m.Events)
        {
            if (string.IsNullOrWhiteSpace(e.Id))
                throw new InvalidDataException($"Манифест {source}: событие с пустым id.");
            if (!seen.Add(e.Id))
                throw new InvalidDataException($"Манифест {source}: дублирующийся id события '{e.Id}'.");
        }
    }
}