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

        // RawJson is the scraper's copy of each API response; no page reads it, and
        // it's most of the database's size.
        List<Fighter> fighters = await source.Fighters.AsNoTracking().ToListAsync();
        fighters.ForEach(f => f.RawJson = null);
        List<Event> events = await source.Events.AsNoTracking().ToListAsync();
        events.ForEach(e => e.RawJson = null);

        await CopyAsync(target, fighters);
        await CopyAsync(target, await source.FighterHeroStats.AsNoTracking().ToListAsync());
        await CopyAsync(target, await source.FighterStats.AsNoTracking().ToListAsync());
        await CopyAsync(target, events);

        // The query filters leave out duplicate events and bouts; also leave out the
        // bouts on those duplicate events, which would otherwise point at nothing.
        HashSet<Guid> eventIds = events.Select(e => e.Id).ToHashSet();
        List<Bout> bouts = (await source.Bouts.AsNoTracking().ToListAsync())
            .Where(b => eventIds.Contains(b.EventId)).ToList();
        HashSet<string> boutIds = bouts.Select(b => b.Id).ToHashSet();
        await CopyAsync(target, bouts);
        await CopyAsync(target, (await source.BoutFighterStats.AsNoTracking().ToListAsync())
            .Where(s => boutIds.Contains(s.BoutId)).ToList());

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
