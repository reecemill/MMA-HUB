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
        ("UFC experience", ["log_fights"]),
        ("Time since last fight", ["log_layoff"]),
        ("Physical", ["age", "height", "reach", "southpaw"]),
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
            ["age"] = p.AgeRef - (SnapshotDate - date).TotalDays / 365.25,
            ["height"] = p.Height,
            ["reach"] = p.Reach,
            ["southpaw"] = p.Southpaw,
        };
    }

    private Prediction Predict(Dictionary<string, double> a, Dictionary<string, double> b)
    {
        Dictionary<string, double> contributions = Model.Features.ToDictionary(
            f => f.Key, f => f.Weight * (a[f.Key] - b[f.Key]) / f.Scale);
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
    [JsonPropertyName("elo_only")] public Score EloOnly { get; set; } = new();
    [JsonPropertyName("better_win_rate")] public Score BetterWinRate { get; set; } = new();
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
