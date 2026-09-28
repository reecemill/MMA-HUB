using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Web.Models;
using Web.Services;

namespace Web.Pages;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly FightPredictor _predictor;

    public Event? NextEvent { get; set; }
    public DateTime DataDate => _predictor.SnapshotDate;

    public IndexModel(AppDbContext db, FightPredictor predictor)
    {
        _db = db;
        _predictor = predictor;
    }

    public async Task OnGetAsync()
    {
        // The site shows a snapshot of the data, so "next" is relative to when it was
        // collected, not today; otherwise a deployed copy would go blank as time passes.
        DateTime from = DataDate < DateTime.UtcNow.Date ? DataDate : DateTime.UtcNow.Date;
        NextEvent = await _db.Events
            .Where(e => e.EventDate != null && e.EventDate > from && e.Status == "scheduled")
            .OrderBy(e => e.EventDate)
            .FirstOrDefaultAsync();
    }
}
