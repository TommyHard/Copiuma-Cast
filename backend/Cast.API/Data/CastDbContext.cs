using Cast.API.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Cast.API.Data;

/// <summary>
/// Контекст БД. Identity с Guid-ключами + доменные сущности
/// </summary>
public sealed class CastDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public CastDbContext(DbContextOptions<CastDbContext> options) : base(options) { }

    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<RoomMembership> RoomMemberships => Set<RoomMembership>();
    public DbSet<EventLogEntry> EventLog => Set<EventLogEntry>();
    public DbSet<CoinTransaction> CoinTransactions => Set<CoinTransaction>();
    public DbSet<StreamerWallet> StreamerWallets => Set<StreamerWallet>();
    public DbSet<RoomConnection> RoomConnections => Set<RoomConnection>();
    public DbSet<Bet> Bets => Set<Bet>();
    public DbSet<BetOutcome> BetOutcomes => Set<BetOutcome>();
    public DbSet<BetWager> BetWagers => Set<BetWager>();
    public DbSet<Game> Games => Set<Game>();
    public DbSet<FriendLink> FriendLinks => Set<FriendLink>();
    public DbSet<Follow> Follows => Set<Follow>();
    public DbSet<UserConnection> UserConnections => Set<UserConnection>();
    public DbSet<MediaItem> MediaItems => Set<MediaItem>();
    public DbSet<StreamerTagFilter> StreamerTagFilters => Set<StreamerTagFilter>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<StreamerFilterSettings> StreamerFilterSettings => Set<StreamerFilterSettings>();
    public DbSet<RoomEventToggle> RoomEventToggles => Set<RoomEventToggle>();
    public DbSet<StreamerApplication> StreamerApplications => Set<StreamerApplication>();
    public DbSet<GameEventOverride> GameEventOverrides => Set<GameEventOverride>();
    public DbSet<NewsPost> News => Set<NewsPost>();
    public DbSet<NewsImage> NewsImages => Set<NewsImage>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<ApplicationUser>(e =>
        {
            e.Property(u => u.DisplayName).HasMaxLength(64);
            e.Property(u => u.Handle).HasMaxLength(30);
            e.HasIndex(u => u.Handle).IsUnique();
        });

        b.Entity<Game>(e =>
        {
            e.HasIndex(g => g.Slug).IsUnique();
            e.Property(g => g.Slug).HasMaxLength(64).IsRequired();
            e.Property(g => g.Title).HasMaxLength(128).IsRequired();
            e.Property(g => g.Genre).HasMaxLength(64);
        });

        b.Entity<FriendLink>(e =>
        {
            e.HasIndex(f => new { f.RequesterId, f.AddresseeId }).IsUnique();
            e.HasIndex(f => f.AddresseeId);
        });

        b.Entity<Follow>(e =>
        {
            e.HasIndex(f => new { f.FollowerId, f.StreamerId }).IsUnique();
            e.HasIndex(f => f.StreamerId);
        });

        b.Entity<UserConnection>(e =>
        {
            e.HasIndex(c => c.ConnectionId).IsUnique();
            e.HasIndex(c => c.UserId);
            e.Property(c => c.ConnectionId).HasMaxLength(128);
        });

        b.Entity<MediaItem>(e =>
        {
            e.Property(m => m.Title).HasMaxLength(200);
            e.HasIndex(m => new { m.OwnerId, m.CreatedAt });
            e.HasIndex(m => m.Status);
        });

        b.Entity<StreamerTagFilter>(e =>
        {
            e.HasIndex(f => new { f.StreamerId, f.Tag }).IsUnique();
            e.Property(f => f.Tag).HasMaxLength(64);
        });

        b.Entity<Tag>(e =>
        {
            e.HasIndex(t => t.Name).IsUnique();
            e.Property(t => t.Name).HasMaxLength(64).IsRequired();
        });

        b.Entity<StreamerFilterSettings>(e =>
        {
            e.HasKey(x => x.StreamerId);
        });

        b.Entity<RoomEventToggle>(e =>
        {
            e.HasIndex(t => new { t.RoomId, t.EventId }).IsUnique();
            e.Property(t => t.EventId).HasMaxLength(64);
        });

        b.Entity<StreamerApplication>(e =>
        {
            e.HasIndex(a => new { a.UserId, a.Status });
            e.Property(a => a.Message).HasMaxLength(1000);
        });

        b.Entity<GameEventOverride>(e =>
        {
            e.HasIndex(o => new { o.GameId, o.EventId }).IsUnique();
            e.Property(o => o.GameId).HasMaxLength(64);
            e.Property(o => o.EventId).HasMaxLength(64);
        });

        b.Entity<NewsPost>(e =>
        {
            e.Property(n => n.Title).HasMaxLength(200);
            e.HasIndex(n => new { n.Published, n.CreatedAt });
        });

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

        b.Entity<CoinTransaction>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.EventId).HasMaxLength(64);
            e.HasIndex(t => new { t.UserId, t.CreatedAt });
            e.HasIndex(t => t.RoomId);
        });

        b.Entity<StreamerWallet>(e =>
        {
            e.HasIndex(w => new { w.UserId, w.StreamerId }).IsUnique();
        });

        b.Entity<RoomConnection>(e =>
        {
            e.HasIndex(c => c.ConnectionId).IsUnique();
            e.HasIndex(c => new { c.RoomId, c.Role });
            e.Property(c => c.ConnectionId).HasMaxLength(128);
        });

        b.Entity<Bet>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(256);
            e.HasIndex(x => new { x.RoomId, x.Status });
            e.HasMany(x => x.Outcomes).WithOne(o => o.Bet!)
                .HasForeignKey(o => o.BetId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Wagers).WithOne(w => w.Bet!)
                .HasForeignKey(w => w.BetId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<BetOutcome>(e => e.Property(x => x.Label).HasMaxLength(128));

        b.Entity<BetWager>(e =>
        {
            e.HasIndex(x => new { x.BetId, x.OutcomeId });
            e.HasIndex(x => x.UserId);
        });
    }
}