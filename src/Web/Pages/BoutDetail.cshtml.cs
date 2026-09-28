using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Web.Models;

namespace Web.Pages;

public class BoutDetailModel : PageModel
{
    private readonly AppDbContext _db;

    public Bout BoutData { get; set; } = null!;
    public string? EventSlug { get; set; }
    public BoutFighterStats? Fighter1FightStats { get; set; }
    public BoutFighterStats? Fighter2FightStats { get; set; }

    public BoutDetailModel(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> OnGetAsync(string id, string? eventSlug)
    {
        Bout? bout = await _db.Bouts
            .Include(b => b.Fighter1).ThenInclude(f => f!.Stats)
            .Include(b => b.Fighter2).ThenInclude(f => f!.Stats)
            .Include(b => b.Stats)
            .FirstOrDefaultAsync(b => b.Id == id);

        if (bout == null)
        {
            return NotFound();
        }

        BoutData = bout;
        EventSlug = eventSlug;

        Fighter1FightStats = bout.Stats.FirstOrDefault(s =>
            (bout.Fighter1Id != null && s.FighterId == bout.Fighter1Id) ||
            (s.FighterSlug != null && s.FighterSlug == bout.Fighter1Slug));

        Fighter2FightStats = bout.Stats.FirstOrDefault(s =>
            (bout.Fighter2Id != null && s.FighterId == bout.Fighter2Id) ||
            (s.FighterSlug != null && s.FighterSlug == bout.Fighter2Slug));

        return Page();
    }
}
