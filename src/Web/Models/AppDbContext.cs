using Microsoft.EntityFrameworkCore;

namespace Web.Models;

// Reads the scraper's UFC.com tables. The table names are the scraper's (Ufc*); the
// classes here map only the columns the site and the model use.
public class AppDbContext : DbContext
{
    public DbSet<Event> Events => Set<Event>();
    public DbSet<Fight> Fights => Set<Fight>();
    public DbSet<FightFighter> FightFighters => Set<FightFighter>();
    public DbSet<Fighter> Fighters => Set<Fighter>();
    public DbSet<FighterBio> FighterBios => Set<FighterBio>();
    public DbSet<FightStats> FightStats => Set<FightStats>();
    public DbSet<FightScore> FightScores => Set<FightScore>();
    public DbSet<Ranking> Rankings => Set<Ranking>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Event>().ToTable("UfcEvents")
            .HasMany(e => e.Fights).WithOne(f => f.Event).HasForeignKey(f => f.EventId);

        modelBuilder.Entity<Fight>().ToTable("UfcFights");
        modelBuilder.Entity<Fight>()
            .HasMany(f => f.Fighters).WithOne(ff => ff.Fight).HasForeignKey(ff => ff.FightId);
        modelBuilder.Entity<Fight>()
            .HasMany(f => f.Stats).WithOne().HasForeignKey(s => s.FightId);
        modelBuilder.Entity<Fight>()
            .HasMany(f => f.Scores).WithOne().HasForeignKey(s => s.FightId);

        modelBuilder.Entity<FightFighter>().ToTable("UfcFightFighters")
            .HasKey(ff => new { ff.FightId, ff.FighterId });
        modelBuilder.Entity<FightFighter>()
            .HasOne(ff => ff.Fighter).WithMany().HasForeignKey(ff => ff.FighterId);

        modelBuilder.Entity<Fighter>().ToTable("UfcFighters")
            .HasOne(f => f.Bio).WithOne().HasForeignKey<FighterBio>(b => b.FighterId);
        modelBuilder.Entity<FighterBio>().ToTable("UfcFighterProfiles").HasKey(b => b.FighterId);

        modelBuilder.Entity<FightStats>().ToTable("UfcFightStats").HasKey(s => new { s.FightId, s.FighterId });
        modelBuilder.Entity<FightScore>().ToTable("UfcFightScores").HasKey(s => new { s.FightId, s.JudgeId, s.FighterId });
        modelBuilder.Entity<Ranking>().ToTable("UfcRankings");
    }
}
