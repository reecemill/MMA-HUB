using Microsoft.AspNetCore.Mvc.RazorPages;
using Web.Services;

namespace Web.Pages;

public class HowItWorksModel : PageModel
{
    public ModelInfo Info { get; }

    public HowItWorksModel(FightPredictor predictor)
    {
        Info = predictor.Model;
    }

    public static string Percent(double fraction) => (fraction * 100).ToString("0.0") + "%";

    // A difference in accuracy as percentage points, e.g. "+3.8".
    public static string Points(double difference) =>
        (difference < 0 ? "−" : "+") + Math.Abs(difference * 100).ToString("0.0");

    // [mean, low, high] of a 95% interval for new minus old, from ml/train.py's paired bootstrap.
    public double[]? Comparison(string name, string metric) =>
        Info.Test.Comparisons.TryGetValue(name, out var byMetric) && byMetric.TryGetValue(metric, out double[]? interval)
            ? interval : null;

    // Lower log loss is better, so an interval entirely below zero means clearly better.
    public static string LogLossVerdict(double[] interval) =>
        interval[2] < 0 ? "clearly better than" : interval[1] > 0 ? "clearly worse than" : "within chance of";

    // Favorites winning more often than predicted (averaged over fights) means the model is cautious.
    public double CalibrationGap =>
        Info.Test.Calibration.Sum(b => b.Bouts * (b.Actual - b.Predicted)) / Math.Max(1, Info.Test.Calibration.Sum(b => b.Bouts));
}
