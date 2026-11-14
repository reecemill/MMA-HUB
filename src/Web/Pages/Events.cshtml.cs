using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Web.Models;

namespace Web.Pages;

public class EventsModel : PageModel
{
    private readonly AppDbContext _db;

    public List<Event> Events { get; set; } = [];
    public List<int> Years { get; set; } = [];
    public int? SelectedYear { get; set; }
    public string? SelectedType { get; set; }
    public string SelectedSort { get; set; } = "desc";

    public EventsModel(AppDbContext db)
    {
        _db = db;
    }

    public async Task OnGetAsync(string? year, string? type, string? sort)
    {
        // No "year" in the URL at all (first visit) -> default to the current year.
        // year=all -> explicitly show every year. Otherwise parse the specific year requested.
        SelectedYear = year switch
        {
            null => DateTime.UtcNow.Year,
            "all" => null,
            _ => int.TryParse(year, out var y) ? y : DateTime.UtcNow.Year
        };
        SelectedType = type;
        SelectedSort = sort == "asc" ? "asc" : "desc";

        Years = await _db.Events
            .Where(e => e.Date != null)
            .Select(e => e.Date!.Value.Year)
            .Distinct()
            .OrderByDescending(y => y)
            .ToListAsync();

        var query = _db.Events.Where(e => e.Date != null);

        if (SelectedYear.HasValue)
        {
            query = query.Where(e => e.Date!.Value.Year == SelectedYear.Value);
        }

        if (type == "numbered")
        {
            query = query.Where(e => !e.Name.Contains("Fight Night"));
        }
        else if (type == "fightnight")
        {
            query = query.Where(e => e.Name.Contains("Fight Night"));
        }

        Events = SelectedSort == "asc"
            ? await query.OrderBy(e => e.Date).ThenBy(e => e.Id).ToListAsync()
            : await query.OrderByDescending(e => e.Date).ThenByDescending(e => e.Id).ToListAsync();
    }
}
