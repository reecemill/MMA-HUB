using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Web.Models;
using Web.Services;

namespace Web.Pages;

public class FightersModel : PageModel
{
    public static readonly string[] DivisionOrder =
    [
        "Flyweight", "Bantamweight", "Featherweight", "Lightweight",
        "Welterweight", "Middleweight", "Light Heavyweight", "Heavyweight",
        "Women's Strawweight", "Women's Flyweight", "Women's Bantamweight", "Women's Featherweight"
    ];

    // A fighter counts as active if they've fought, or have a fight booked, within this
    // long of the snapshot date.
    public static readonly TimeSpan ActiveWindow = TimeSpan.FromDays(2 * 365);

    private readonly AppDbContext _db;
    private readonly FightPredictor _predictor;

    public List<Fighter> Fighters { get; set; } = [];
    public List<string> Divisions { get; set; } = [];
    public string? SelectedDivision { get; set; }
    public string? SearchTerm { get; set; }
    public string SelectedStatus { get; set; } = "active";

    // Each ranked fighter's rank in their own division; 0 is the champion.
    public Dictionary<int, int> Ranks { get; set; } = [];

    public FightersModel(AppDbContext db, FightPredictor predictor)
    {
        _db = db;
        _predictor = predictor;
    }

    public async Task OnGetAsync(string? division, string? search, string? status)
    {
        SelectedDivision = division;
        SearchTerm = search;
        SelectedStatus = status == "all" ? "all" : "active";

        var divisions = await _db.Fighters
            .Where(f => f.WeightClass != null)
            .Select(f => f.WeightClass!)
            .Distinct()
            .ToListAsync();
        Divisions = divisions
            .OrderBy(d => Array.IndexOf(DivisionOrder, d) is var i && i >= 0 ? i : int.MaxValue)
            .ThenBy(d => d)
            .ToList();

        Ranks = await DivisionRanks(_db);

        var query = _db.Fighters.AsQueryable();

        if (SelectedStatus == "active")
        {
            DateTime since = _predictor.SnapshotDate - ActiveWindow;
            query = query.Where(f => _db.FightFighters.Any(ff => ff.FighterId == f.Id && ff.Fight!.Event!.StartTime >= since));
        }

        if (!string.IsNullOrWhiteSpace(division))
        {
            query = query.Where(f => f.WeightClass == division);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            // ToLower().Contains works on both Postgres and the SQLite snapshot, unlike ILike.
            string term = search.Trim().ToLower();
            query = query.Where(f => (f.FirstName + " " + f.LastName).ToLower().Contains(term));
        }

        List<Fighter> fighters = await query.OrderBy(f => f.LastName).ThenBy(f => f.FirstName).ToListAsync();
        Fighters = SelectedStatus == "active"
            ? fighters.OrderBy(f => Ranks.TryGetValue(f.Id, out int r) ? r : int.MaxValue).ThenBy(f => f.LastName).ThenBy(f => f.FirstName).ToList()
            : fighters;
    }

    // Fighter id -> rank in their division (0 = champion), leaving out the pound-for-pound lists.
    public static async Task<Dictionary<int, int>> DivisionRanks(AppDbContext db)
    {
        var ranked = await db.Rankings
            .Where(r => r.FighterId != null && !r.Division.Contains("Pound-for-Pound"))
            .Select(r => new { r.FighterId, r.Rank })
            .ToListAsync();
        return ranked.GroupBy(r => r.FighterId!.Value).ToDictionary(g => g.Key, g => g.Min(r => r.Rank));
    }
}
