using Cast.MediaProcessing.Worker.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cast.MediaProcessing.Worker.Data;

/// <summary>
/// Контекст воркера поверх общей БД. Маппит только таблицу MediaItems (узкая
/// проекция). Схему владеет и мигрирует Cast.API — воркер сюда не пишет миграций
/// </summary>
public sealed class MediaDbContext : DbContext
{
    public MediaDbContext(DbContextOptions<MediaDbContext> options) : base(options) { }

    public DbSet<MediaItem> MediaItems => Set<MediaItem>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<MediaItem>(e =>
        {
            e.ToTable("MediaItems");
            e.HasKey(x => x.Id);
        });
    }
}