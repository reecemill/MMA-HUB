using System.Text.Json;
using System.Text.Json.Serialization;

namespace Web.Models;

public class Fighter
{
    public Guid Id { get; set; }
    public string? Slug { get; set; }
    public string? UfcStatsId { get; set; }
    public string? Name { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Nickname { get; set; }
    public string? Division { get; set; }
    public string? Status { get; set; }
    public bool IsActive { get; set; }
    public string? ChampionStatus { get; set; }
    public int? P4pRank { get; set; }
    public int? Rank { get; set; }
    public int? RecordWins { get; set; }
    public int? RecordLosses { get; set; }
    public int? RecordDraws { get; set; }
    public int? RecordNoContest { get; set; }
    public string? RecordText { get; set; }
    public string? Country { get; set; }
    public string? PlaceOfBirth { get; set; }
    public string? TrainsAt { get; set; }
    public string? FightingStyle { get; set; }
    public int? Age { get; set; }
    public string? HeightInches { get; set; }
    public string? WeightLbs { get; set; }
    public string? ReachInches { get; set; }
    public string? LegReachInches { get; set; }
    public string? Stance { get; set; }
    public DateTime? OctagonDebut { get; set; }
    public string? ProfileUrl { get; set; }
    public string? ImageUrl { get; set; }
    public string? Bio { get; set; }
    public string? RawJson { get; set; }
    public List<FighterHeroStat> HeroStatRows { get; set; } = [];
    public FighterStats? Stats { get; set; }
}

public class FighterHeroStat
{
    public int Id { get; set; }
    public Guid FighterId { get; set; }
    public string StatKey { get; set; } = "";
    public string StatValue { get; set; } = "";
}

public class FighterStats
{
    public Guid FighterId { get; set; }
    public int SigStrikesLanded { get; set; }
    public int SigStrikesAttempted { get; set; }
    public decimal StrikingAccuracy { get; set; }
    public decimal SigStrikesLandedPerMin { get; set; }
    public decimal SigStrikesAbsorbedPerMin { get; set; }
    public decimal SigStrikesDefense { get; set; }
    public int TakedownsLanded { get; set; }
    public int TakedownsAttempted { get; set; }
    public decimal TakedownAccuracy { get; set; }
    public decimal TakedownAvgPer15Min { get; set; }
    public decimal TakedownDefense { get; set; }
    public decimal SubmissionAvgPer15Min { get; set; }
    public decimal KnockdownAvg { get; set; }
    public int AverageFightTimeSeconds { get; set; }
    public decimal StandingStrikePercent { get; set; }
    public decimal ClinchStrikePercent { get; set; }
    public decimal GroundStrikePercent { get; set; }
    public decimal HeadStrikePercent { get; set; }
    public decimal BodyStrikePercent { get; set; }
    public decimal LegStrikePercent { get; set; }
}
