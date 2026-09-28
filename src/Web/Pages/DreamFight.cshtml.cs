using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Web.Models;
using Web.Services;

namespace Web.Pages;

public class DreamFightModel : PageModel
{
    public static readonly (string A, string B)[] Examples =
    [
        ("jon-jones", "alex-pereira"),
        ("khabib-nurmagomedov", "georges-st-pierre"),
        ("anderson-silva", "israel-adesanya"),
        ("conor-mcgregor", "ilia-topuria"),
    ];

    private readonly AppDbContext _db;
    private readonly FightPredictor _predictor;

    // Every fighter with at least one UFC fight, as the text shown in the picker.
    public List<string> Options { get; set; } = [];
    public string? InputA { get; set; }
    public string? InputB { get; set; }
    public Fighter? FighterA { get; set; }
    public Fighter? FighterB { get; set; }
    public FighterProfile? ProfileA { get; set; }
    public FighterProfile? ProfileB { get; set; }
    public Prediction? Result { get; set; }
    public string? Error { get; set; }
    public Dictionary<string, string> ExampleNames { get; set; } = [];

    public DreamFightModel(AppDbContext db, FightPredictor predictor)
    {
        _db = db;
        _predictor = predictor;
    }

    // a and b can be slugs (from links) or the picker's "Name (Division)" text.
    public async Task OnGetAsync(string? a, string? b)
    {
        List<Fighter> fighters = (await _db.Fighters.AsNoTracking()
                .Select(f => new Fighter { Id = f.Id, Name = f.Name, Slug = f.Slug, Division = f.Division })
                .ToListAsync())
            .Where(f => f.Name != null && _predictor.Profile(f.Slug)?.Fights > 0)
            .ToList();

        // Same-named fighters get the one with more UFC fights for the bare name.
        Dictionary<string, Fighter> byLabel = [];
        foreach (Fighter f in fighters.OrderByDescending(f => _predictor.Profile(f.Slug)!.Fights))
        {
            byLabel.TryAdd(Label(f), f);
            byLabel.TryAdd(f.Name!, f);
        }
        Options = fighters.Select(Label).Distinct().OrderBy(l => l).ToList();

        Dictionary<string, Fighter> bySlug = fighters.ToDictionary(f => f.Slug!);
        foreach (var (x, y) in Examples)
        {
            if (bySlug.TryGetValue(x, out Fighter? fx)) ExampleNames[x] = fx.Name!;
            if (bySlug.TryGetValue(y, out Fighter? fy)) ExampleNames[y] = fy.Name!;
        }

        if (string.IsNullOrWhiteSpace(a) && string.IsNullOrWhiteSpace(b)) return;

        Fighter? Resolve(string? input) =>
            string.IsNullOrWhiteSpace(input) ? null
            : bySlug.GetValueOrDefault(input.Trim()) ?? byLabel.GetValueOrDefault(input.Trim())
              ?? byLabel.FirstOrDefault(kv => kv.Key.Equals(input.Trim(), StringComparison.OrdinalIgnoreCase)).Value;

        FighterA = Resolve(a);
        FighterB = Resolve(b);
        InputA = FighterA != null ? Label(FighterA) : a;
        InputB = FighterB != null ? Label(FighterB) : b;

        if (FighterA == null || FighterB == null)
        {
            string missing = FighterA == null ? a ?? "" : b ?? "";
            Error = string.IsNullOrWhiteSpace(missing)
                ? "Pick two fighters."
                : $"Couldn't find \"{missing}\" among fighters with a UFC fight. Try picking from the list.";
            return;
        }
        if (FighterA.Id == FighterB.Id)
        {
            Error = "Pick two different fighters.";
            return;
        }

        // Full rows for the tale of the tape.
        FighterA = await _db.Fighters.AsNoTracking().FirstAsync(f => f.Id == FighterA.Id);
        FighterB = await _db.Fighters.AsNoTracking().FirstAsync(f => f.Id == FighterB.Id);
        ProfileA = _predictor.Profile(FighterA.Slug);
        ProfileB = _predictor.Profile(FighterB.Slug);
        Result = _predictor.PredictDreamFight(FighterA.Slug, FighterB.Slug);
    }

    public static string Label(Fighter f) =>
        string.IsNullOrWhiteSpace(f.Division) ? f.Name! : $"{f.Name} ({f.Division})";

    // Bar width for a reason, relative to the biggest one shown.
    public double ReasonWidth(Reason r) =>
        Result == null ? 0 : 100 * Math.Abs(r.Contribution) / Result.Reasons.Max(x => Math.Abs(x.Contribution));
}
