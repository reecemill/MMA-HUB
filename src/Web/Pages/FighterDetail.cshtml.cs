using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Web.Models;

namespace Web.Pages;

public class FighterDetailModel : PageModel
{
    private readonly AppDbContext _db;

    public Fighter FighterData { get; set; } = null!;

    public string? WinMethodChartCss { get; set; }
    public List<(string Label, int Value, string Color)> WinMethodLegend { get; set; } = [];

    public List<(string Label, int Value)> SummaryStats { get; set; } = [];

    public string? StrikingAccuracyGradient { get; set; }
    public string? TakedownAccuracyGradient { get; set; }

    public List<(string Label, decimal Percent)> StrikePositionBreakdown { get; set; } = [];
    public List<(string Label, decimal Percent)> StrikeTargetBreakdown { get; set; } = [];

    public FighterDetailModel(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> OnGetAsync(string slug)
    {
        Fighter? fighter = await _db.Fighters
            .Include(f => f.HeroStatRows)
            .Include(f => f.Stats)
            .FirstOrDefaultAsync(f => f.Slug == slug);

        if (fighter == null)
        {
            return NotFound();
        }

        FighterData = fighter;

        BuildWinMethodChart();
        BuildSummaryStats();
        BuildStatsGrid();

        return Page();
    }

    private void BuildWinMethodChart()
    {
        int koWins = GetHeroStatInt("wins by knockout");
        int subWins = GetHeroStatInt("wins by submission");

        // Not enough data from the hero stats to say anything meaningful about method of victory.
        if (koWins == 0 && subWins == 0) return;

        int totalWins = FighterData.RecordWins ?? 0;
        int decisionWins = Math.Max(0, totalWins - koWins - subWins);
        int total = koWins + subWins + decisionWins;
        if (total == 0) return;

        List<(string Label, int Value, string Color)> segments =
        [
            ("KO/TKO", koWins, "var(--chart-ko)"),
            ("Submission", subWins, "var(--chart-sub)"),
            ("Decision", decisionWins, "var(--chart-neutral)")
        ];
        segments = segments.Where(s => s.Value > 0).ToList();

        WinMethodChartCss = BuildConicGradient(segments, total);
        WinMethodLegend = segments;
    }

    private void BuildSummaryStats()
    {
        SummaryStats =
        [
            ("Wins by Knockout", GetHeroStatInt("wins by knockout")),
            ("Wins by Submission", GetHeroStatInt("wins by submission")),
            ("First Round Finishes", GetHeroStatInt("first round finishes"))
        ];
        SummaryStats = SummaryStats.Where(s => s.Value > 0).ToList();
    }

    private void BuildStatsGrid()
    {
        FighterStats? stats = FighterData.Stats;
        if (stats == null) return;

        int strikingAccuracyPercent = (int)Math.Round(stats.StrikingAccuracy * 100);
        StrikingAccuracyGradient = BuildConicGradient(
            [("Filled", strikingAccuracyPercent, "var(--accent)"), ("Remainder", 100 - strikingAccuracyPercent, "var(--border)")],
            100);

        int takedownAccuracyPercent = (int)Math.Round(stats.TakedownAccuracy * 100);
        TakedownAccuracyGradient = BuildConicGradient(
            [("Filled", takedownAccuracyPercent, "var(--accent)"), ("Remainder", 100 - takedownAccuracyPercent, "var(--border)")],
            100);

        StrikePositionBreakdown =
        [
            ("Standing", stats.StandingStrikePercent),
            ("Clinch", stats.ClinchStrikePercent),
            ("Ground", stats.GroundStrikePercent)
        ];

        StrikeTargetBreakdown =
        [
            ("Head", stats.HeadStrikePercent),
            ("Body", stats.BodyStrikePercent),
            ("Leg", stats.LegStrikePercent)
        ];
    }

    public static string FormatPercent(decimal fraction) => (fraction * 100).ToString("0") + "%";

    public static string FormatFightTime(int seconds)
    {
        TimeSpan span = TimeSpan.FromSeconds(seconds);
        return $"{(int)span.TotalMinutes}:{span.Seconds:00}";
    }

    private int GetHeroStatInt(string key)
    {
        FighterHeroStat? stat = FighterData.HeroStatRows
            .FirstOrDefault(s => s.StatKey.Equals(key, StringComparison.OrdinalIgnoreCase));

        return stat != null && int.TryParse(stat.StatValue, out int value) ? value : 0;
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
