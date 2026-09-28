using System.Text.Json;
using System.Text.Json.Serialization;

namespace Web.Services;

// Win probabilities from the model trained by ml/train.py. That script writes the
// model's weights and every fighter's latest features to Data/; this class only
// combines them, so the feature definitions live in one place (the Python).
public class FightPredictor
{
    // Features are shown to visitors in groups, since several of them measure the same
    // thing (win rate, last three, and streak are all "form") and can pull opposite ways
    // individually once the model has accounted for the others.
    private static readonly (string Label, string[] Keys)[] ReasonGroups =
    [
        ("Elo rating", ["elo"]),
        ("Record and form", ["win_pct", "last3", "streak"]),
        ("Finishing", ["ko_rate", "sub_rate", "finished_rate"]),
        ("Striking", ["slpm", "sapm", "str_acc", "str_def", "kd15", "strike_matchup"]),
        ("Grappling", ["td15", "td_acc", "td_def", "ctrl", "sub15", "grapple_matchup"]),
        ("UFC experience", ["log_fights"]),
        ("Time since last fight", ["log_layoff"]),
        ("Age", ["age"]),
        ("Physical", ["height", "reach", "southpaw"]),
    ];

    public ModelInfo Model { get; }
    private readonly Dictionary<string, FighterProfile> _profiles;
    private readonly Dictionary<string, double> _pastPredictions;

    public FightPredictor(IWebHostEnvironment env)
    {
        string dir = Path.Combine(env.ContentRootPath, "Data");
        JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true };
        Model = JsonSerializer.Deserialize<ModelInfo>(File.ReadAllText(Path.Combine(dir, "model.json")), options)!;
        _profiles = JsonSerializer.Deserialize<Dictionary<string, FighterProfile>>(
            File.ReadAllText(Path.Combine(dir, "fighter_profiles.json")), options)!;
        _pastPredictions = JsonSerializer.Deserialize<PastPredictions>(
            File.ReadAllText(Path.Combine(dir, "past_predictions.json")), options)!.Fighter1WinProbability;
    }

    // UTC so it can be compared with event dates in Postgres queries.
    public DateTime SnapshotDate => DateTime.SpecifyKind(Model.SnapshotDate, DateTimeKind.Utc);

    public FighterProfile? Profile(string? slug) =>
        slug != null && _profiles.TryGetValue(slug, out FighterProfile? p) ? p : null;

    // What the held-out test model picked before a 2024+ bout, as P(fighter 1 wins).
    public double? PastPrediction(string boutId) =>
        _pastPredictions.TryGetValue(boutId, out double p) ? p : null;

    // For an upcoming bout on a known date. Null if either fighter isn't in the data.
    public Prediction? PredictBout(string? slug1, string? slug2, DateTime date)
    {
        FighterProfile? a = Profile(slug1), b = Profile(slug2);
        return a == null || b == null ? null : Predict(Features(a, date), Features(b, date));
    }

    // A matchup that never happened: each fighter as of their last UFC fight, so a
    // retired fighter isn't marked down for years of inactivity.
    public Prediction? PredictDreamFight(string? slug1, string? slug2)
    {
        FighterProfile? a = Profile(slug1), b = Profile(slug2);
        if (a == null || b == null) return null;
        return Predict(Features(a, a.LastFight ?? SnapshotDate), Features(b, b.LastFight ?? SnapshotDate));
    }

    private Dictionary<string, double> Features(FighterProfile p, DateTime date)
    {
        double layoffDays = p.LastFight.HasValue ? Math.Max((date - p.LastFight.Value).TotalDays, 0) : 0;
        // Age from the date of birth when it's known; UFC.com's age is stale for retired fighters.
        double age = p.Birth.HasValue
            ? (date.Date - p.Birth.Value.Date).Days / 365.25
            : p.AgeRef - (SnapshotDate - date).TotalDays / 365.25;
        return new Dictionary<string, double>
        {
            ["elo"] = p.Elo,
            ["log_fights"] = p.LogFights,
            ["win_pct"] = p.WinPct,
            ["last3"] = p.Last3,
            ["streak"] = p.Streak,
            ["ko_rate"] = p.KoRate,
            ["sub_rate"] = p.SubRate,
            ["finished_rate"] = p.FinishedRate,
            ["log_layoff"] = Math.Log(1 + layoffDays),
            ["age"] = age,
            ["height"] = p.Height,
            ["reach"] = p.Reach,
            ["southpaw"] = p.Southpaw,
            ["slpm"] = p.Slpm,
            ["sapm"] = p.Sapm,
            ["str_acc"] = p.StrAcc,
            ["str_def"] = p.StrDef,
            ["kd15"] = p.Kd15,
            ["td15"] = p.Td15,
            ["td_acc"] = p.TdAcc,
            ["td_def"] = p.TdDef,
            ["ctrl"] = p.Ctrl,
            ["sub15"] = p.Sub15,
        };
    }

    // Fighter A's features minus fighter B's, plus the matchup terms (A's attack against
    // B's defense, minus the reverse), exactly as pair_features in ml/train.py.
    private static Dictionary<string, double> Differences(Dictionary<string, double> a, Dictionary<string, double> b)
    {
        Dictionary<string, double> d = a.Keys.ToDictionary(k => k, k => a[k] - b[k]);
        d["strike_matchup"] = a["slpm"] * (1 - b["str_def"]) - b["slpm"] * (1 - a["str_def"]);
        d["grapple_matchup"] = a["td15"] * (1 - b["td_def"]) - b["td15"] * (1 - a["td_def"]);
        return d;
    }

    private Prediction Predict(Dictionary<string, double> a, Dictionary<string, double> b)
    {
        Dictionary<string, double> d = Differences(a, b);
        Dictionary<string, double> contributions = Model.Features.ToDictionary(
            f => f.Key, f => f.Weight * d[f.Key] / f.Scale);
        double logit = contributions.Values.Sum();

        List<Reason> reasons = ReasonGroups
            .Select(g => new Reason(g.Label, g.Keys.Where(contributions.ContainsKey).Sum(k => contributions[k])))
            .Where(r => Math.Abs(r.Contribution) >= 0.01)
            .OrderByDescending(r => Math.Abs(r.Contribution))
            .ToList();

        return new Prediction(1 / (1 + Math.Exp(-logit)), reasons);
    }
}

public record Prediction(double Fighter1WinProbability, List<Reason> Reasons)
{
    public double Fighter2WinProbability => 1 - Fighter1WinProbability;
}

// Contribution is on the log-odds scale: positive favors fighter 1.
public record Reason(string Label, double Contribution);

public class FighterProfile
{
    public double Elo { get; set; }
    [JsonPropertyName("log_fights")] public double LogFights { get; set; }
    [JsonPropertyName("win_pct")] public double WinPct { get; set; }
    public double Last3 { get; set; }
    public double Streak { get; set; }
    [JsonPropertyName("ko_rate")] public double KoRate { get; set; }
    [JsonPropertyName("sub_rate")] public double SubRate { get; set; }
    [JsonPropertyName("finished_rate")] public double FinishedRate { get; set; }
    public double Height { get; set; }
    public double Reach { get; set; }
    public double Southpaw { get; set; }
    public double AgeRef { get; set; }
    public DateTime? Birth { get; set; }
    // Rates over earlier UFC fights with ufcstats.com stats, pulled toward the league
    // average for fighters with little fight time (see FighterHistory.state in ml/train.py).
    public double Slpm { get; set; }
    public double Sapm { get; set; }
    [JsonPropertyName("str_acc")] public double StrAcc { get; set; }
    [JsonPropertyName("str_def")] public double StrDef { get; set; }
    public double Kd15 { get; set; }
    public double Td15 { get; set; }
    [JsonPropertyName("td_acc")] public double TdAcc { get; set; }
    [JsonPropertyName("td_def")] public double TdDef { get; set; }
    public double Ctrl { get; set; }
    public double Sub15 { get; set; }
    public int Fights { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Draws { get; set; }
    public int KoWins { get; set; }
    public int SubWins { get; set; }
    public int TimesFinished { get; set; }
    public string Recent { get; set; } = "";
    public DateTime? LastFight { get; set; }

    public string UfcRecord => Draws > 0 ? $"{Wins}-{Losses}-{Draws}" : $"{Wins}-{Losses}";
}

public class ModelInfo
{
    public string TrainedOn { get; set; } = "";
    public DateTime SnapshotDate { get; set; }
    public int TrainingBouts { get; set; }
    public int EloK { get; set; }
    public string FeatureSet { get; set; } = "";
    public List<ModelFeature> Features { get; set; } = [];
    public TestResults Test { get; set; } = new();
}

public class ModelFeature
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public double Scale { get; set; }
    public double Weight { get; set; }
}

public class TestResults
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public Score Model { get; set; } = new();
    [JsonPropertyName("before_fight_stats")] public Score BeforeFightStats { get; set; } = new();
    [JsonPropertyName("elo_only")] public Score EloOnly { get; set; } = new();
    [JsonPropertyName("better_win_rate")] public Score BetterWinRate { get; set; } = new();
    // Each model family's pick on the validation years, scored on the test years.
    public Dictionary<string, Score> Families { get; set; } = [];
    // New minus old per metric, as [mean, low, high] of a 95% paired-bootstrap interval.
    public Dictionary<string, Dictionary<string, double[]>> Comparisons { get; set; } = [];
    public List<CalibrationBucket> Calibration { get; set; } = [];
}

public class Score
{
    public double Accuracy { get; set; }
    [JsonPropertyName("log_loss")] public double? LogLoss { get; set; }
    public double? Brier { get; set; }
    public int Bouts { get; set; }
}

public class CalibrationBucket
{
    public double From { get; set; }
    public double To { get; set; }
    public int Bouts { get; set; }
    public double Predicted { get; set; }
    public double Actual { get; set; }
}

class PastPredictions
{
    public Dictionary<string, double> Fighter1WinProbability { get; set; } = [];
}
