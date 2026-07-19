using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Web.Models;

namespace Web.Pages;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;

    public int FighterCount { get; set; }
    public int EventCount { get; set; }

    public IndexModel(AppDbContext db)
    {
        _db = db;
    }

    public async Task OnGetAsync()
    {
        FighterCount = await _db.Fighters.CountAsync();
        EventCount = await _db.Events.CountAsync();
    }
}
