using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Web.Models;

namespace Web.Pages;

public class EventsModel : PageModel
{
    private readonly AppDbContext _db;

    public List<Event> Events { get; set; } = [];

    public EventsModel(AppDbContext db)
    {
        _db = db;
    }

    public async Task OnGetAsync()
    {
        Events = await _db.Events
            .OrderByDescending(e => e.EventDate)
            .ThenByDescending(e => e.CreatedAt)
            .ToListAsync();
    }
}
