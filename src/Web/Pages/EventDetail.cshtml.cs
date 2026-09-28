using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Web.Models;
using Web.Services;

namespace Web.Pages;

public class EventDetailModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly FightPredictor _predictor;

    public Event EventData { get; set; } = null!;
    public List<IGrouping<string, Bout>> BoutsBySection { get; set; } = [];

    // Upcoming bouts: the current model's prediction. Past bouts in the test years: what
    // the held-out model picked before the fight. Both as P(fighter 1 wins).
    public Dictionary<string, double> Predictions { get; set; } = [];
    public bool ShowsPastPicks { get; set; }

    public EventDetailModel(AppDbContext db, FightPredictor predictor)
    {
        _db = db;
        _predictor = predictor;
    }

    public async Task<IActionResult> OnGetAsync(string slug)
    {
        Event? ev = await _db.Events
            .Include(e => e.Bouts).ThenInclude(b => b.Fighter1)
            .Include(e => e.Bouts).ThenInclude(b => b.Fighter2)
            .FirstOrDefaultAsync(e => e.Slug == slug);

        if (ev == null)
        {
            return NotFound();
        }

        EventData = ev;

        BoutsBySection = ev.Bouts
            .OrderBy(b => b.BoutOrder ?? int.MaxValue)
            .GroupBy(b => b.CardSection ?? "Card")
            .OrderBy(g => g.Min(b => b.CardSectionOrder ?? int.MaxValue))
            .ToList();

        foreach (Bout bout in ev.Bouts.Where(b => !b.IsCancelled))
        {
            double? p = bout.Status == "completed"
                ? _predictor.PastPrediction(bout.Id)
                : _predictor.PredictBout(bout.Fighter1?.Slug, bout.Fighter2?.Slug, ev.EventDate ?? _predictor.SnapshotDate)
                    ?.Fighter1WinProbability;
            if (p.HasValue) Predictions[bout.Id] = p.Value;
        }
        ShowsPastPicks = ev.Bouts.Any(b => b.Status == "completed" && Predictions.ContainsKey(b.Id));

        return Page();
    }

    // The favorite and their chance, and whether that pick was right once there's a result.
    public (string Favorite, int Percent, bool? Correct)? Pick(Bout bout)
    {
        if (!Predictions.TryGetValue(bout.Id, out double p1)) return null;
        bool favorsFighter1 = p1 >= 0.5;
        string name = (favorsFighter1 ? bout.Fighter1Name ?? bout.Fighter1?.Name : bout.Fighter2Name ?? bout.Fighter2?.Name) ?? "TBD";
        Guid? favoriteId = favorsFighter1 ? bout.Fighter1Id : bout.Fighter2Id;
        bool? correct = bout.WinnerFighterId.HasValue ? bout.WinnerFighterId == favoriteId : null;
        return (name, (int)Math.Round(Math.Max(p1, 1 - p1) * 100), correct);
    }
}
