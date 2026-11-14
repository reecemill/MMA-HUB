using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

// Scrapes UFC.com into Postgres. See UfcCom/UfcComScraper.cs for the options.
IConfiguration config = new ConfigurationBuilder()
    .AddUserSecrets<Program>()
    .Build();

await UfcComScraper.Run(config["ConnectionStrings:MmaDb"], args);

class AppDbContext : DbContext
{
    // Filled from UFC.com by UfcComScraper; see UfcCom/UfcComEntities.cs.
    public DbSet<UfcEvent> UfcEvents => Set<UfcEvent>();
    public DbSet<UfcFight> UfcFights => Set<UfcFight>();
    public DbSet<UfcFightFighter> UfcFightFighters => Set<UfcFightFighter>();
    public DbSet<UfcFighter> UfcFighters => Set<UfcFighter>();
    public DbSet<UfcFightStats> UfcFightStats => Set<UfcFightStats>();
    public DbSet<UfcRoundStats> UfcRoundStats => Set<UfcRoundStats>();
    public DbSet<UfcFightScore> UfcFightScores => Set<UfcFightScore>();
    public DbSet<UfcRoundScore> UfcRoundScores => Set<UfcRoundScore>();
    public DbSet<UfcFightAction> UfcFightActions => Set<UfcFightAction>();
    public DbSet<UfcFighterProfile> UfcFighterProfiles => Set<UfcFighterProfile>();
    public DbSet<UfcRanking> UfcRankings => Set<UfcRanking>();

    private readonly string _connectionString;

    public AppDbContext(string connectionString)
    {
        _connectionString = connectionString;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseNpgsql(_connectionString);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        UfcComModel.Configure(modelBuilder);

        var utcConverter = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))
                {
                    property.SetValueConverter(utcConverter);
                }
            }
        }
    }
}

class AppDbContextFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddUserSecrets<Program>()
            .Build();

        string connectionString = config["ConnectionStrings:MmaDb"]!;
        return new AppDbContext(connectionString);
    }
}
