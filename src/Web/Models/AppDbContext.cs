using Microsoft.EntityFrameworkCore;

namespace Web.Models;

public class AppDbContext : DbContext
{
    public DbSet<Fighter> Fighters => Set<Fighter>();
    public DbSet<FighterHeroStat> FighterHeroStats => Set<FighterHeroStat>();
    public DbSet<FighterStats> FighterStats => Set<FighterStats>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<Bout> Bouts => Set<Bout>();
    public DbSet<BoutFighterStats> BoutFighterStats => Set<BoutFighterStats>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // jsonb is Postgres-only; the SQLite snapshot stores RawJson as plain text.
        if (Database.IsNpgsql())
        {
            modelBuilder.Entity<Fighter>().Property(f => f.RawJson).HasColumnType("jsonb");
            modelBuilder.Entity<Event>().Property(e => e.RawJson).HasColumnType("jsonb");
        }

        // Every query skips events and bouts the scraper marked as duplicates.
        modelBuilder.Entity<Event>().HasQueryFilter(e => e.DuplicateOfEventId == null);
        modelBuilder.Entity<Bout>().HasQueryFilter(b => b.DuplicateOfBoutId == null);

        modelBuilder.Entity<FighterHeroStat>()
            .HasOne<Fighter>()
            .WithMany(f => f.HeroStatRows)
            .HasForeignKey(s => s.FighterId);

        modelBuilder.Entity<FighterStats>().HasKey(s => s.FighterId);
        modelBuilder.Entity<Fighter>()
            .HasOne(f => f.Stats)
            .WithOne()
            .HasForeignKey<FighterStats>(s => s.FighterId);

        modelBuilder.Entity<Event>()
            .HasMany(e => e.Bouts)
            .WithOne()
            .HasForeignKey(b => b.EventId);

        modelBuilder.Entity<Bout>()
            .HasOne(b => b.Fighter1)
            .WithMany()
            .HasForeignKey(b => b.Fighter1Id);
        modelBuilder.Entity<Bout>()
            .HasOne(b => b.Fighter2)
            .WithMany()
            .HasForeignKey(b => b.Fighter2Id);

        modelBuilder.Entity<Bout>()
            .HasMany(b => b.Stats)
            .WithOne()
            .HasForeignKey(s => s.BoutId);
    }
}
