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
    public List<IGrouping<string, Fight>> FightsBySection { get; set; } = [];

    // Upcoming fights: the current model's prediction. Past fights in the test years: what
    // the held-out model picked before the fight. Both as P(red corner wins).
    public Dictionary<int, double> Predictions { get; set; } = [];
    public bool ShowsPastPicks { get; set; }

    public EventDetailModel(AppDbContext db, FightPredictor predictor)
    {
        _db = db;
        _predictor = predictor;
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        Event? ev = await _db.Events
            .Include(e => e.Fights).ThenInclude(f => f.Fighters).ThenInclude(ff => ff.Fighter)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (ev == null)
        {
            return NotFound();
        }

        EventData = ev;

        FightsBySection = ev.Fights
            .OrderBy(f => f.FightOrder ?? int.MaxValue)
            .GroupBy(f => f.CardSection)
            .OrderBy(g => g.Min(f => f.CardSectionOrder))
            .ToList();

        DateTime date = ev.Date?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) ?? _predictor.SnapshotDate;
        foreach (Fight fight in ev.Fights)
        {
            double? p = fight.IsFinished
                ? _predictor.PastPrediction(fight.Id)
                : _predictor.PredictBout(fight.Red?.Fighter?.Slug, fight.Blue?.Fighter?.Slug, date)?.Fighter1WinProbability;
            if (p.HasValue) Predictions[fight.Id] = p.Value;
        }
        ShowsPastPicks = ev.Fights.Any(f => f.IsFinished && Predictions.ContainsKey(f.Id));

        return Page();
    }

    // The favorite and their chance, and whether that pick was right once there's a result.
    public (string Favorite, int Percent, bool? Correct)? Pick(Fight fight)
    {
        if (!Predictions.TryGetValue(fight.Id, out double pRed)) return null;
        FightFighter? favorite = pRed >= 0.5 ? fight.Red : fight.Blue;
        bool? correct = fight.WinnerFighterId.HasValue ? fight.WinnerFighterId == favorite?.FighterId : null;
        return (favorite?.Fighter?.Name ?? "TBD", (int)Math.Round(Math.Max(pRed, 1 - pRed) * 100), correct);
    }
}
