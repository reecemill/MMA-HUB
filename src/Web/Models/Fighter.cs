namespace Web.Models;

public class Fighter
{
    public int Id { get; set; }
    // Unique; the fighter's page is /FighterDetail/{Slug}, as on ufc.com/athlete/{Slug}.
    public string Slug { get; set; } = "";
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? NickName { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? BornCity { get; set; }
    public string? BornCountry { get; set; }
    public string? Stance { get; set; }
    public decimal? HeightInches { get; set; }
    public decimal? ReachInches { get; set; }
    public decimal? WeightLbs { get; set; }
    public string? WeightClass { get; set; }
    // The fighter's whole professional record, not just the UFC part.
    public int? Wins { get; set; }
    public int? Losses { get; set; }
    public int? Draws { get; set; }
    public int? NoContests { get; set; }

    // Only for fighters whose ufc.com page the scraper has fetched.
    public FighterBio? Bio { get; set; }

    public string Name => $"{FirstName} {LastName}".Trim();

    // "18-2-0", plus no contests when there are any.
    public string? Record => Wins == null ? null
        : $"{Wins}-{Losses ?? 0}-{Draws ?? 0}" + (NoContests > 0 ? $" ({NoContests} NC)" : "");

    public string? Born => Format.Join(BornCity, BornCountry);

    public int? AgeOn(DateOnly date) =>
        DateOfBirth is DateOnly birth ? date.Year - birth.Year - (date < birth.AddYears(date.Year - birth.Year) ? 1 : 0) : null;
}

// From the fighter's page on ufc.com (the scraper's UfcFighterProfiles table).
public class FighterBio
{
    public int FighterId { get; set; }
    public string? Status { get; set; }
    public string? TrainsAt { get; set; }
    public string? FightingStyle { get; set; }
    public DateOnly? OctagonDebut { get; set; }
    public DateTime FetchedAt { get; set; }
}

// ufc.com/rankings when the data was collected. Rank 0 is the champion.
public class Ranking
{
    public int Id { get; set; }
    public string Division { get; set; } = "";
    public int Rank { get; set; }
    public string? RankChange { get; set; }
    public string FighterSlug { get; set; } = "";
    public string? FighterName { get; set; }
    public int? FighterId { get; set; }
    public DateTime FetchedAt { get; set; }

    public bool IsPoundForPound => Division.Contains("Pound-for-Pound");
}
