using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Web.Models;
using Web.Services;

namespace Web.Pages;

public class FighterDetailModel : PageModel
{
    private static readonly HashSet<string> KoMethods = ["KO/TKO", "TKO - Doctor's Stoppage"];

    private readonly AppDbContext _db;
    private readonly FightPredictor _predictor;

    public Fighter FighterData { get; set; } = null!;
    public int? Rank { get; set; }
    public bool IsActive { get; set; }
    public int? Age { get; set; }

    // UFC fights, newest first.
    public List<Fight> FightHistory { get; set; } = [];

    public string? WinMethodChartCss { get; set; }
    public List<(string Label, int Value, string Color)> WinMethodLegend { get; set; } = [];
    public List<(string Label, int Value)> SummaryStats { get; set; } = [];

    // Career numbers added up from the fighter's UFC fights with stats; null if none have any.
    public CareerStats? Career { get; set; }
    public string? StrikingAccuracyGradient { get; set; }
    public string? TakedownAccuracyGradient { get; set; }
    public List<(string Label, double Percent)> StrikePositionBreakdown { get; set; } = [];
    public List<(string Label, double Percent)> StrikeTargetBreakdown { get; set; } = [];

    public FighterDetailModel(AppDbContext db, FightPredictor predictor)
    {
        _db = db;
        _predictor = predictor;
    }

    public async Task<IActionResult> OnGetAsync(string slug)
    {
        Fighter? fighter = await _db.Fighters
            .Include(f => f.Bio)
            .FirstOrDefaultAsync(f => f.Slug == slug);

        if (fighter == null)
        {
            return NotFound();
        }

        FighterData = fighter;
        Rank = (await FightersModel.DivisionRanks(_db)).TryGetValue(fighter.Id, out int rank) ? rank : null;
        Age = fighter.AgeOn(DateOnly.FromDateTime(_predictor.SnapshotDate));

        FightHistory = await _db.Fights
            .Include(f => f.Event)
            .Include(f => f.Fighters).ThenInclude(ff => ff.Fighter)
            .Include(f => f.Stats)
            .Where(f => f.Fighters.Any(ff => ff.FighterId == fighter.Id))
            .OrderByDescending(f => f.Event!.StartTime).ThenByDescending(f => f.Id)
            .ToListAsync();

        DateTime since = _predictor.SnapshotDate - FightersModel.ActiveWindow;
        IsActive = FightHistory.Any(f => f.Event?.StartTime >= since);

        BuildWinMethodChart();
        BuildCareerStats();

        return Page();
    }

    // This fighter's side of a fight: result, and the opponent.
    public (string Result, Fighter? Opponent) Side(Fight fight)
    {
        FightFighter? own = fight.Fighters.FirstOrDefault(ff => ff.FighterId == FighterData.Id);
        Fighter? opponent = fight.Fighters.FirstOrDefault(ff => ff.FighterId != FighterData.Id)?.Fighter;
        string result = !fight.IsFinished ? "Upcoming" : own?.Outcome ?? Format.Missing;
        return (result, opponent);
    }

    private void BuildWinMethodChart()
    {
        List<Fight> wins = FightHistory.Where(f => f.IsFinished && f.WinnerFighterId == FighterData.Id).ToList();
        int koWins = wins.Count(f => KoMethods.Contains(f.Method ?? ""));
        int subWins = wins.Count(f => f.Method == "Submission");
        int decisionWins = wins.Count(f => f.Method?.StartsWith("Decision") == true);
        int otherWins = wins.Count - koWins - subWins - decisionWins;
        int firstRoundFinishes = wins.Count(f => f.EndingRound == 1 && (KoMethods.Contains(f.Method ?? "") || f.Method == "Submission"));

        SummaryStats =
        [
            ("Wins by Knockout", koWins),
            ("Wins by Submission", subWins),
            ("First Round Finishes", firstRoundFinishes)
        ];
        SummaryStats = SummaryStats.Where(s => s.Value > 0).ToList();

        if (wins.Count == 0) return;
        List<(string Label, int Value, string Color)> segments =
        [
            ("KO/TKO", koWins, "var(--chart-ko)"),
            ("Submission", subWins, "var(--chart-sub)"),
            ("Decision", decisionWins, "var(--chart-neutral)"),
            ("Other", otherWins, "var(--border)")
        ];
        segments = segments.Where(s => s.Value > 0).ToList();
        WinMethodChartCss = BuildConicGradient(segments, wins.Count);
        WinMethodLegend = segments;
    }

    private void BuildCareerStats()
    {
        var career = new CareerStats();
        foreach (Fight fight in FightHistory.Where(f => f.IsFinished && f.DurationSeconds > 0))
        {
            FightStats? own = fight.StatsFor(FighterData.Id);
            FightStats? against = fight.Stats.FirstOrDefault(s => s.FighterId != FighterData.Id);
            if (own == null || against == null) continue;
            career.Add(own, against, fight.DurationSeconds!.Value);
        }
        if (career.Fights == 0) return;
        Career = career;

        int strikingPercent = (int)Math.Round(career.StrikingAccuracy * 100);
        StrikingAccuracyGradient = BuildConicGradient(
            [("Filled", strikingPercent, "var(--accent)"), ("Remainder", 100 - strikingPercent, "var(--border)")], 100);
        int takedownPercent = (int)Math.Round(career.TakedownAccuracy * 100);
        TakedownAccuracyGradient = BuildConicGradient(
            [("Filled", takedownPercent, "var(--accent)"), ("Remainder", 100 - takedownPercent, "var(--border)")], 100);

        StrikePositionBreakdown =
        [
            ("Standing", career.Share(career.SigDistanceLanded)),
            ("Clinch", career.Share(career.SigClinchLanded)),
            ("Ground", career.Share(career.SigGroundLanded))
        ];
        StrikeTargetBreakdown =
        [
            ("Head", career.Share(career.SigHeadLanded)),
            ("Body", career.Share(career.SigBodyLanded)),
            ("Leg", career.Share(career.SigLegLanded))
        ];
    }

    private static string BuildConicGradient(List<(string Label, int Value, string Color)> segments, int total)
    {
        List<string> parts = [];
        double cumulative = 0;

        foreach (var segment in segments)
        {
            double start = cumulative;
            double end = cumulative + (double)segment.Value / total * 100;
            parts.Add($"{segment.Color} {start.ToString("0.##")}% {end.ToString("0.##")}%");
            cumulative = end;
        }

        return string.Join(", ", parts);
    }
}

// Totals over a fighter's UFC fights with stats, and the rates ufc.com shows from them.
public class CareerStats
{
    public int Fights { get; private set; }
    public int Seconds { get; private set; }
    public int Knockdowns { get; private set; }
    public int SigLanded { get; private set; }
    public int SigAttempted { get; private set; }
    public int SigAbsorbed { get; private set; }
    public int SigThrownAtMe { get; private set; }
    public int SigHeadLanded { get; private set; }
    public int SigBodyLanded { get; private set; }
    public int SigLegLanded { get; private set; }
    public int SigDistanceLanded { get; private set; }
    public int SigClinchLanded { get; private set; }
    public int SigGroundLanded { get; private set; }
    public int TakedownsLanded { get; private set; }
    public int TakedownsAttempted { get; private set; }
    public int TakedownsAgainstLanded { get; private set; }
    public int TakedownsAgainstAttempted { get; private set; }
    public int SubmissionAttempts { get; private set; }

    public void Add(FightStats own, FightStats against, int seconds)
    {
        Fights++;
        Seconds += seconds;
        Knockdowns += own.Knockdowns;
        SigLanded += own.SigStrikesLanded;
        SigAttempted += own.SigStrikesAttempted;
        SigAbsorbed += against.SigStrikesLanded;
        SigThrownAtMe += against.SigStrikesAttempted;
        SigHeadLanded += own.SigHeadStrikesLanded;
        SigBodyLanded += own.SigBodyStrikesLanded;
        SigLegLanded += own.SigLegStrikesLanded;
        SigDistanceLanded += own.SigDistanceStrikesLanded;
        SigClinchLanded += own.SigClinchStrikesLanded;
        SigGroundLanded += own.SigGroundStrikesLanded;
        TakedownsLanded += own.TakedownsLanded;
        TakedownsAttempted += own.TakedownsAttempted;
        TakedownsAgainstLanded += against.TakedownsLanded;
        TakedownsAgainstAttempted += against.TakedownsAttempted;
        SubmissionAttempts += own.SubmissionsAttempted;
    }

    public double Minutes => Seconds / 60.0;
    public double SigLandedPerMinute => Minutes > 0 ? SigLanded / Minutes : 0;
    public double SigAbsorbedPerMinute => Minutes > 0 ? SigAbsorbed / Minutes : 0;
    public double StrikingAccuracy => Ratio(SigLanded, SigAttempted);
    public double StrikingDefense => 1 - Ratio(SigAbsorbed, SigThrownAtMe);
    public double TakedownAccuracy => Ratio(TakedownsLanded, TakedownsAttempted);
    public double TakedownDefense => 1 - Ratio(TakedownsAgainstLanded, TakedownsAgainstAttempted);
    public double TakedownsPer15 => Per15(TakedownsLanded);
    public double SubmissionsPer15 => Per15(SubmissionAttempts);
    public double KnockdownsPer15 => Per15(Knockdowns);
    public int AverageFightSeconds => Fights > 0 ? Seconds / Fights : 0;

    // A kind of strike as a share of all significant strikes landed.
    public double Share(int landed) => Ratio(landed, SigLanded);

    private double Per15(int count) => Minutes > 0 ? count / Minutes * 15 : 0;
    private static double Ratio(int part, int whole) => whole > 0 ? (double)part / whole : 0;
}
