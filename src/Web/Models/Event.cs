using System.Text.Json;
using System.Text.Json.Serialization;

namespace Web.Models;

public class Event
{
    public Guid Id { get; set; }
    public string? Slug { get; set; }
    public string? Title { get; set; }
    public string? ShortTitle { get; set; }
    public string? Status { get; set; }
    public DateTime? StartsAt { get; set; }
    public string? Venue { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? LocationText { get; set; }
    public string? ImageUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public string? DataId { get; set; }
    public bool HasStats { get; set; }
    public DateTime? DataFreshness { get; set; }
    public string? FreshnessStatus { get; set; }
    public int? DataAgeHours { get; set; }
    public string? DataSource { get; set; }
    public string? Warning { get; set; }
    public DateTime? EventDate { get; set; }
    public string? EventDateLabel { get; set; }
    public string? EventWeekday { get; set; }
    public string? EventTimeZone { get; set; }
    public string? VenueTimeZone { get; set; }
    public DateTime? VenueDate { get; set; }
    public string? VenueDateLabel { get; set; }
    public string? VenueWeekday { get; set; }
    public string? RawJson { get; set; }
}
