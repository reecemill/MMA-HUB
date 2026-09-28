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
    public double? DataAgeHours { get; set; }
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
    // Set by the scraper when Cito lists the same event twice; the site hides these.
    public Guid? DuplicateOfEventId { get; set; }
    public List<Bout> Bouts { get; set; } = [];
}

public class Bout
{
    public string Id { get; set; } = "";
    public Guid EventId { get; set; }
    public string? CardSection { get; set; }
    public int? CardSectionOrder { get; set; }
    public int? BoutOrder { get; set; }
    public string? WeightClass { get; set; }
    public bool TitleBout { get; set; }
    public string? Status { get; set; }
    public bool IsCancelled { get; set; }
    public int? ResultRound { get; set; }
    public string? ResultTime { get; set; }
    public string? Method { get; set; }

    public Guid? Fighter1Id { get; set; }
    public string? Fighter1Slug { get; set; }
    public string? Fighter1Name { get; set; }
    public string? Fighter1Corner { get; set; }
    public string? Fighter1Outcome { get; set; }
    public Fighter? Fighter1 { get; set; }

    public Guid? Fighter2Id { get; set; }
    public string? Fighter2Slug { get; set; }
    public string? Fighter2Name { get; set; }
    public string? Fighter2Corner { get; set; }
    public string? Fighter2Outcome { get; set; }
    public Fighter? Fighter2 { get; set; }

    public Guid? WinnerFighterId { get; set; }
    // Set by the scraper when this fight is also listed on an earlier event.
    public string? DuplicateOfBoutId { get; set; }

    public List<BoutFighterStats> Stats { get; set; } = [];
}

public class BoutFighterStats
{
    public string Id { get; set; } = "";
    public string BoutId { get; set; } = "";
    public Guid? FighterId { get; set; }
    public string? FighterSlug { get; set; }
    public int Knockdowns { get; set; }
    public int SigStrikesLanded { get; set; }
    public int SigStrikesAttempted { get; set; }
    public int TotalStrikesLanded { get; set; }
    public int TotalStrikesAttempted { get; set; }
    public int TakedownsLanded { get; set; }
    public int TakedownsAttempted { get; set; }
    public int SubmissionAttempts { get; set; }
    public int Reversals { get; set; }
    public string? ControlTime { get; set; }
    public int HeadLanded { get; set; }
    public int HeadAttempted { get; set; }
    public int BodyLanded { get; set; }
    public int BodyAttempted { get; set; }
    public int LegLanded { get; set; }
    public int LegAttempted { get; set; }
    public int DistanceLanded { get; set; }
    public int DistanceAttempted { get; set; }
    public int ClinchLanded { get; set; }
    public int ClinchAttempted { get; set; }
    public int GroundLanded { get; set; }
    public int GroundAttempted { get; set; }
}
