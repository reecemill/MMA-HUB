using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Web.Models;
using Web.Services;

namespace Web.Pages;

public class FightDetailModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly FightPredictor _predictor;

    public Fight FightData { get; set; } = null!;
    public Fighter? Red { get; set; }
    public Fighter? Blue { get; set; }
    public FightStats? RedStats { get; set; }
    public FightStats? BlueStats { get; set; }

    // Each judge's totals, for fights that went to the scorecards.
    public List<(string Judge, int? Red, int? Blue)> Scorecards { get; set; } = [];

    public FightDetailModel(AppDbContext db, FightPredictor predictor)
    {
        _db = db;
        _predictor = predictor;
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        Fight? fight = await _db.Fights
            .Include(f => f.Event)
            .Include(f => f.Fighters).ThenInclude(ff => ff.Fighter)
            .Include(f => f.Stats)
            .Include(f => f.Scores)
            .FirstOrDefaultAsync(f => f.Id == id);

        if (fight == null)
        {
            return NotFound();
        }

        FightData = fight;
        Red = fight.Red?.Fighter;
        Blue = fight.Blue?.Fighter;
        RedStats = fight.StatsFor(Red?.Id);
        BlueStats = fight.StatsFor(Blue?.Id);

        Scorecards = fight.Scores
            .GroupBy(s => s.JudgeId)
            .Select(g => (g.First().JudgeName ?? "Judge",
                g.FirstOrDefault(s => s.FighterId == Red?.Id)?.Score,
                g.FirstOrDefault(s => s.FighterId == Blue?.Id)?.Score))
            .ToList();

        return Page();
    }

    public DateOnly FightDate => FightData.Event?.Date ?? DateOnly.FromDateTime(_predictor.SnapshotDate);

    // "12 of 30"
    public static string LandedOf(int landed, int attempted) => $"{landed} of {attempted}";
}
