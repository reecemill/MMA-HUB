using Microsoft.EntityFrameworkCore;
using Web.Models;

namespace Web;

// Copies the Postgres database into a single SQLite file, which the deployed site
// serves read-only and the prediction model trains on. Run from src/Web:
//   dotnet run -- export-sqlite ../../data/mma.db
public static class SnapshotExporter
{
    public static async Task RunAsync(string postgresConnectionString, string sqlitePath)
    {
        string fullPath = Path.GetFullPath(sqlitePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        if (File.Exists(fullPath)) File.Delete(fullPath);

        await using var source = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(postgresConnectionString).Options);
        await using var target = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={fullPath}").Options);
        await target.Database.EnsureCreatedAsync();
        target.ChangeTracker.AutoDetectChangesEnabled = false;

        // Parents before children, since the SQLite file enforces the foreign keys.
        await CopyAsync(target, await source.Events.AsNoTracking().ToListAsync());
        await CopyAsync(target, await source.Fighters.AsNoTracking().ToListAsync());
        await CopyAsync(target, await source.FighterBios.AsNoTracking().ToListAsync());
        await CopyAsync(target, await source.Fights.AsNoTracking().ToListAsync());
        await CopyAsync(target, await source.FightFighters.AsNoTracking().ToListAsync());
        await CopyAsync(target, await source.FightStats.AsNoTracking().ToListAsync());
        await CopyAsync(target, await source.FightScores.AsNoTracking().ToListAsync());
        await CopyAsync(target, await source.Rankings.AsNoTracking().ToListAsync());

        Console.WriteLine($"Wrote {fullPath} ({new FileInfo(fullPath).Length / 1024 / 1024} MB)");
    }

    private static async Task CopyAsync<T>(AppDbContext target, List<T> rows) where T : class
    {
        target.Set<T>().AddRange(rows);
        await target.SaveChangesAsync();
        target.ChangeTracker.Clear();
        Console.WriteLine($"{typeof(T).Name}: {rows.Count}");
    }
}
