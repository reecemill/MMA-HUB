using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Web.Models;

namespace Web.Pages;

public class FightersModel : PageModel
{
    private static readonly string[] DivisionOrder =
    [
        "Flyweight", "Bantamweight", "Featherweight", "Lightweight",
        "Welterweight", "Middleweight", "Light Heavyweight", "Heavyweight",
        "Women's Strawweight", "Women's Flyweight", "Women's Bantamweight"
    ];

    private readonly AppDbContext _db;

    public List<Fighter> Fighters { get; set; } = [];
    public List<string> Divisions { get; set; } = [];
    public string? SelectedDivision { get; set; }
    public string? SearchTerm { get; set; }
    public string SelectedStatus { get; set; } = "active";

    public FightersModel(AppDbContext db)
    {
        _db = db;
    }

    public async Task OnGetAsync(string? division, string? search, string? status)
    {
        SelectedDivision = division;
        SearchTerm = search;
        SelectedStatus = status == "all" ? "all" : "active";

        var divisions = await _db.Fighters
            .Where(f => f.Division != null)
            .Select(f => f.Division!)
            .Distinct()
            .ToListAsync();

        Divisions = divisions
            .OrderBy(d => Array.IndexOf(DivisionOrder, d) is var i && i >= 0 ? i : int.MaxValue)
            .ThenBy(d => d)
            .ToList();

        var query = _db.Fighters.AsQueryable();

        if (SelectedStatus == "active")
        {
            query = query.Where(f => f.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(division))
        {
            query = query.Where(f => f.Division == division);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(f => f.Name != null && EF.Functions.ILike(f.Name, $"%{search}%"));
        }

        Fighters = SelectedStatus == "active"
            ? await query.OrderBy(f => f.Rank == null).ThenBy(f => f.Rank).ThenBy(f => f.Name).ToListAsync()
            : await query.OrderBy(f => f.Name).ToListAsync();
    }
}
