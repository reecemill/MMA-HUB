using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Web.Models;

namespace Web.Pages;

public class EventDetailModel : PageModel
{
    private readonly AppDbContext _db;

    public Event EventData { get; set; } = null!;
    public List<IGrouping<string, Bout>> BoutsBySection { get; set; } = [];

    public EventDetailModel(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> OnGetAsync(string slug)
    {
        Event? ev = await _db.Events
            .Include(e => e.Bouts).ThenInclude(b => b.Fighter1)
            .Include(e => e.Bouts).ThenInclude(b => b.Fighter2)
            .FirstOrDefaultAsync(e => e.Slug == slug);

        if (ev == null)
        {
            return NotFound();
        }

        EventData = ev;

        BoutsBySection = ev.Bouts
            .OrderBy(b => b.BoutOrder ?? int.MaxValue)
            .GroupBy(b => b.CardSection ?? "Card")
            .OrderBy(g => g.Min(b => b.CardSectionOrder ?? int.MaxValue))
            .ToList();

        return Page();
    }
}
