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
}
