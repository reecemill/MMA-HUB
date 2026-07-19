using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Web.Models;

namespace Web.Pages;

public class FightersModel : PageModel
{
    private readonly AppDbContext _db;

    public List<Fighter> Fighters { get; set; } = [];

    public FightersModel(AppDbContext db)
    {
        _db = db;
    }

    public async Task OnGetAsync()
    {
        Fighters = await _db.Fighters
            .OrderBy(f => f.Name)
            .ToListAsync();
    }
}
