using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Web.Models;

namespace Web.Pages;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;

    public Event? NextEvent { get; set; }

    public IndexModel(AppDbContext db)
    {
        _db = db;
    }

    public async Task OnGetAsync()
    {
        NextEvent = await _db.Events
            .Where(e => e.EventDate != null && e.EventDate > DateTime.UtcNow && e.Status == "scheduled")
            .OrderBy(e => e.EventDate)
            .FirstOrDefaultAsync();
    }
}
