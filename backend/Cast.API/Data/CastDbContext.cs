using Cast.API.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Cast.API.Data;

/// <summary>
/// Контекст БД. Identity с Guid-ключами + доменные сущности (комнаты, членство,
/// журнал событий). Провайдер — PostgreSQL (Npgsql), настраивается в Program.cs
/// </summary>
public sealed class CastDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public CastDbContext(DbContextOptions<CastDbContext> options) : base(options) { }

    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<RoomMembership> RoomMemberships => Set<RoomMembership>();
    public DbSet<EventLogEntry> EventLog => Set<EventLogEntry>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<Room>(e =>
        {
            e.HasIndex(r => r.Code).IsUnique();
            e.Property(r => r.Code).HasMaxLength(16).IsRequired();
            e.Property(r => r.Title).HasMaxLength(128);
            e.HasOne(r => r.Owner)
                .WithMany()
                .HasForeignKey(r => r.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<RoomMembership>(e =>
        {
            e.HasIndex(m => new { m.RoomId, m.UserId }).IsUnique();
            e.HasOne(m => m.Room)
                .WithMany(r => r.Memberships)
                .HasForeignKey(m => m.RoomId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(m => m.User)
                .WithMany()
                .HasForeignKey(m => m.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<EventLogEntry>(e =>
        {
            e.HasIndex(x => new { x.RoomId, x.CreatedAt });
            e.Property(x => x.EventId).HasMaxLength(64);
            e.Property(x => x.Username).HasMaxLength(64);
        });
    }
}
