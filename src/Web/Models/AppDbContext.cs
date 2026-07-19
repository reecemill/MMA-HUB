using Microsoft.EntityFrameworkCore;

namespace Web.Models;

public class AppDbContext : DbContext
{
    public DbSet<Fighter> Fighters => Set<Fighter>();
    public DbSet<FighterHeroStat> FighterHeroStats => Set<FighterHeroStat>();
    public DbSet<Event> Events => Set<Event>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Fighter>().Property(f => f.RawJson).HasColumnType("jsonb");
        modelBuilder.Entity<Event>().Property(e => e.RawJson).HasColumnType("jsonb");

        modelBuilder.Entity<FighterHeroStat>()
            .HasOne<Fighter>()
            .WithMany(f => f.HeroStatRows)
            .HasForeignKey(s => s.FighterId);
    }
}
