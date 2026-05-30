using Cast.API.Data;
using Cast.API.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cast.API.Tags;

/// <summary>
/// Справочник тегов: поиск существующих и идемпотентное создание новых.
/// Имена нормализуются в нижний регистр; уникальность — на индексе
/// </summary>
public sealed class TagService
{
    private readonly CastDbContext _db;

    public TagService(CastDbContext db) => _db = db;

    public static string Normalize(string tag) => tag.Trim().ToLowerInvariant();

    /// <summary>
    /// Поиск тегов по подстроке (для подсказок). Пустой запрос — самые частые/первые
    /// </summary>
    public async Task<List<string>> SearchAsync(string? query, int limit = 20, CancellationToken ct = default)
    {
        var q = _db.Tags.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var s = Normalize(query);
            q = q.Where(t => t.Name.Contains(s));
        }

        return await q.OrderBy(t => t.Name)
            .Take(Math.Clamp(limit, 1, 100))
            .Select(t => t.Name)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Гарантирует наличие тегов в справочнике (создаёт недостающие).
    /// Возвращает нормализованный список без дублей
    /// </summary>
    public async Task<List<string>> EnsureAsync(IEnumerable<string> tags, CancellationToken ct = default)
    {
        var names = tags.Select(Normalize)
            .Where(s => s.Length > 0)
            .Distinct()
            .ToList();
        if (names.Count == 0)
            return names;

        var existing = await _db.Tags
            .Where(t => names.Contains(t.Name))
            .Select(t => t.Name)
            .ToListAsync(ct);

        var toAdd = names.Except(existing).Select(n => new Tag { Name = n }).ToList();
        if (toAdd.Count > 0)
        {
            _db.Tags.AddRange(toAdd);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                foreach (var t in toAdd)
                    _db.Entry(t).State = EntityState.Detached;
            }
        }

        return names;
    }
}
