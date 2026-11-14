using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

// Tables filled from UFC.com, keyed by UFC.com's own ids. Every scraper run rebuilds
// them from the saved files, so nothing here is edited by hand.

class UfcEvent
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DateTime? StartTime { get; set; }

    // The date where the event took place (StartTime is in UTC).
    public DateOnly? Date { get; set; }
    public string? TimeZone { get; set; }
    public string? Status { get; set; }
    public int? VenueId { get; set; }
    public string? Venue { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? CountryCode { get; set; }
}

class UfcFight
{
    public int Id { get; set; }
    public int EventId { get; set; }
    public int? FightOrder { get; set; }
    public string? Status { get; set; }
    public string? CardSegment { get; set; }
    public DateTime? CardSegmentStartTime { get; set; }
    public string? CardSegmentBroadcaster { get; set; }
    public int? WeightClassId { get; set; }
    public string? WeightClass { get; set; }
    public bool IsTitleFight { get; set; }
    public string? TitleName { get; set; }
    public int? ScheduledRounds { get; set; }
    public string? RuleSet { get; set; }
    public int? RefereeId { get; set; }
    public string? Referee { get; set; }

    public int? WinnerFighterId { get; set; }
    public string? Method { get; set; }
    public int? EndingRound { get; set; }
    public string? EndingTime { get; set; }

    // Fight length worked out from the ending round and time and the round lengths in
    // RuleSet ("3 Rnd (5-5-5)"); null when the rule set doesn't say.
    public int? DurationSeconds { get; set; }
    public string? EndingStrike { get; set; }
    public string? EndingTarget { get; set; }
    public string? EndingPosition { get; set; }
    public string? EndingSubmission { get; set; }
    public string? EndingNotes { get; set; }
    public bool FightOfTheNight { get; set; }

    // From the fight JSON; false while UFC.com's stats are still unofficial.
    public bool? OfficialStats { get; set; }
}

class UfcFightFighter
{
    public int FightId { get; set; }
    public int FighterId { get; set; }
    public string? Corner { get; set; }
    public string? Outcome { get; set; }
    public decimal? WeighIn { get; set; }
    public bool KOOfTheNight { get; set; }
    public bool SubmissionOfTheNight { get; set; }
    public bool PerformanceOfTheNight { get; set; }
}

// Taken from the fighter's most recent fight in the API, so the record, weight class
// and measurements are current ones.
class UfcFighter
{
    public int Id { get; set; }
    public int? MmaId { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? NickName { get; set; }

    // The athlete page's address (ufc.com/athlete/{Slug}), from the API's UFCLink. Unique,
    // since the site uses it in its own addresses too (see FinishFighters).
    public string? Slug { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? BornCity { get; set; }
    public string? BornState { get; set; }
    public string? BornCountry { get; set; }
    public string? FightingOutOfCity { get; set; }
    public string? FightingOutOfState { get; set; }
    public string? FightingOutOfCountry { get; set; }
    public string? Stance { get; set; }
    public decimal? HeightInches { get; set; }
    public decimal? ReachInches { get; set; }
    public decimal? WeightLbs { get; set; }
    public string? WeightClass { get; set; }
    public int? Wins { get; set; }
    public int? Losses { get; set; }
    public int? Draws { get; set; }
    public int? NoContests { get; set; }
}

// One fighter's numbers for a whole fight (UfcFightStats) or one round (UfcRoundStats).
// Also the JSON shape: the API's keys match these names, except the times, which come
// as "4:48" and are stored as seconds.
abstract class UfcStatLine
{
    public int Knockdowns { get; set; }

    public int TotalStrikesAttempted { get; set; }
    public int TotalStrikesLanded { get; set; }
    public int TotalHeadStrikesAttempted { get; set; }
    public int TotalHeadStrikesLanded { get; set; }
    public int TotalBodyStrikesAttempted { get; set; }
    public int TotalBodyStrikesLanded { get; set; }
    public int TotalLegStrikesAttempted { get; set; }
    public int TotalLegStrikesLanded { get; set; }
    public int TotalClinchStrikesAttempted { get; set; }
    public int TotalClinchStrikesLanded { get; set; }
    public int TotalGroundStrikesAttempted { get; set; }
    public int TotalGroundStrikesLanded { get; set; }

    public int SigStrikesAttempted { get; set; }
    public int SigStrikesLanded { get; set; }
    public decimal? SigStrikesAccuracy { get; set; }
    public int SigHeadStrikesAttempted { get; set; }
    public int SigHeadStrikesLanded { get; set; }
    public int SigBodyStrikesAttempted { get; set; }
    public int SigBodyStrikesLanded { get; set; }
    public int SigLegStrikesAttempted { get; set; }
    public int SigLegStrikesLanded { get; set; }
    public int SigDistanceStrikesAttempted { get; set; }
    public int SigDistanceStrikesLanded { get; set; }
    public int SigClinchStrikesAttempted { get; set; }
    public int SigClinchStrikesLanded { get; set; }
    public int SigGroundStrikesAttempted { get; set; }
    public int SigGroundStrikesLanded { get; set; }
    public int SigDistanceHeadStrikesAttempted { get; set; }
    public int SigDistanceHeadStrikesLanded { get; set; }
    public int SigDistanceBodyStrikesAttempted { get; set; }
    public int SigDistanceBodyStrikesLanded { get; set; }
    public int SigDistanceLegStrikesAttempted { get; set; }
    public int SigDistanceLegStrikesLanded { get; set; }
    public int SigClinchHeadStrikesAttempted { get; set; }
    public int SigClinchHeadStrikesLanded { get; set; }
    public int SigClinchBodyStrikesAttempted { get; set; }
    public int SigClinchBodyStrikesLanded { get; set; }
    public int SigClinchLegStrikesAttempted { get; set; }
    public int SigClinchLegStrikesLanded { get; set; }
    public int SigGroundHeadStrikesAttempted { get; set; }
    public int SigGroundHeadStrikesLanded { get; set; }
    public int SigGroundBodyStrikesAttempted { get; set; }
    public int SigGroundBodyStrikesLanded { get; set; }
    public int SigGroundLegStrikesAttempted { get; set; }
    public int SigGroundLegStrikesLanded { get; set; }

    public int TakedownsAttempted { get; set; }
    public int TakedownsLanded { get; set; }
    public decimal? TakedownsAccuracy { get; set; }
    public int Reversals { get; set; }
    public int SubmissionsAttempted { get; set; }

    [JsonPropertyName("NeutralTime"), JsonConverter(typeof(ClockSecondsConverter))]
    public int NeutralSeconds { get; set; }
    [JsonPropertyName("StandingTime"), JsonConverter(typeof(ClockSecondsConverter))]
    public int StandingSeconds { get; set; }
    [JsonPropertyName("DistanceTime"), JsonConverter(typeof(ClockSecondsConverter))]
    public int DistanceSeconds { get; set; }
    [JsonPropertyName("ClinchTime"), JsonConverter(typeof(ClockSecondsConverter))]
    public int ClinchSeconds { get; set; }
    [JsonPropertyName("GroundTime"), JsonConverter(typeof(ClockSecondsConverter))]
    public int GroundSeconds { get; set; }
    [JsonPropertyName("ControlTime"), JsonConverter(typeof(ClockSecondsConverter))]
    public int ControlSeconds { get; set; }
    [JsonPropertyName("ClinchControlTime"), JsonConverter(typeof(ClockSecondsConverter))]
    public int ClinchControlSeconds { get; set; }
    [JsonPropertyName("GroundControlTime"), JsonConverter(typeof(ClockSecondsConverter))]
    public int GroundControlSeconds { get; set; }
    [JsonPropertyName("MiscGroundControlTime"), JsonConverter(typeof(ClockSecondsConverter))]
    public int MiscGroundControlSeconds { get; set; }
    [JsonPropertyName("GuardControlTime"), JsonConverter(typeof(ClockSecondsConverter))]
    public int GuardControlSeconds { get; set; }
    [JsonPropertyName("HalfGuardControlTime"), JsonConverter(typeof(ClockSecondsConverter))]
    public int HalfGuardControlSeconds { get; set; }
    [JsonPropertyName("SideControlTime"), JsonConverter(typeof(ClockSecondsConverter))]
    public int SideControlSeconds { get; set; }
    [JsonPropertyName("MountControlTime"), JsonConverter(typeof(ClockSecondsConverter))]
    public int MountControlSeconds { get; set; }
    [JsonPropertyName("BackControlTime"), JsonConverter(typeof(ClockSecondsConverter))]
    public int BackControlSeconds { get; set; }
}

class UfcFightStats : UfcStatLine
{
    public int FightId { get; set; }
    public int FighterId { get; set; }
}

class UfcRoundStats : UfcStatLine
{
    public int FightId { get; set; }
    public int FighterId { get; set; }

    [JsonPropertyName("RoundNumber")]
    public int Round { get; set; }
}

// A judge's total for one fighter (only on fights that went to the scorecards).
class UfcFightScore
{
    public int FightId { get; set; }
    public int JudgeId { get; set; }
    public int FighterId { get; set; }
    public string? JudgeName { get; set; }
    public int? Score { get; set; }
}

// A judge's score for one fighter in one round, including the finished rounds of
// fights that ended early.
class UfcRoundScore
{
    public int FightId { get; set; }
    public int JudgeId { get; set; }
    public int Round { get; set; }
    public int FighterId { get; set; }
    public string? JudgeName { get; set; }
    public int? Score { get; set; }
}

class UfcFightAction
{
    public int Id { get; set; }
    public int FightId { get; set; }
    public int? FighterId { get; set; }
    public string? Type { get; set; }
    public int? Round { get; set; }
    public string? RoundTime { get; set; }
    public DateTime? Timestamp { get; set; }
}

// From the athlete page on ufc.com: bio, and the career numbers UFC.com shows. These
// are totals as of FetchedAt, so they include every fight up to then.
class UfcFighterProfile
{
    public int FighterId { get; set; }
    public string Slug { get; set; } = "";
    public DateTime FetchedAt { get; set; }

    public string? Status { get; set; }
    public string? Division { get; set; }
    public int? DivisionRank { get; set; }
    public int? PoundForPoundRank { get; set; }
    public bool IsChampion { get; set; }
    public string? RecordText { get; set; }
    public string? PlaceOfBirth { get; set; }
    public string? TrainsAt { get; set; }
    public string? FightingStyle { get; set; }
    public int? Age { get; set; }
    public decimal? HeightInches { get; set; }
    public decimal? WeightLbs { get; set; }
    public decimal? ReachInches { get; set; }
    public decimal? LegReachInches { get; set; }
    public DateOnly? OctagonDebut { get; set; }
    public string? ImageUrl { get; set; }

    public int? WinsByKnockout { get; set; }
    public int? WinsBySubmission { get; set; }
    public int? WinsByDecision { get; set; }
    public int? FirstRoundFinishes { get; set; }

    public int? SigStrikesLanded { get; set; }
    public int? SigStrikesAttempted { get; set; }
    public decimal? StrikingAccuracy { get; set; }
    public int? TakedownsLanded { get; set; }
    public int? TakedownsAttempted { get; set; }
    public decimal? TakedownAccuracy { get; set; }
    public decimal? SigStrikesLandedPerMin { get; set; }
    public decimal? SigStrikesAbsorbedPerMin { get; set; }
    public decimal? TakedownAvgPer15Min { get; set; }
    public decimal? SubmissionAvgPer15Min { get; set; }
    public decimal? SigStrikeDefense { get; set; }
    public decimal? TakedownDefense { get; set; }
    public decimal? KnockdownAvg { get; set; }
    public int? AverageFightSeconds { get; set; }

    public int? SigStrikesStanding { get; set; }
    public int? SigStrikesClinch { get; set; }
    public int? SigStrikesGround { get; set; }
    public int? SigStrikesHead { get; set; }
    public int? SigStrikesBody { get; set; }
    public int? SigStrikesLeg { get; set; }
}

// ufc.com/rankings as of FetchedAt. Rank 0 is the champion; pound-for-pound lists
// have no champion, and tied fighters share a rank. RankChange is since the last
// update: "+1", "-2", or "NR" for new to the rankings. FighterId is null when the
// slug didn't match a UfcFighter.
class UfcRanking
{
    public int Id { get; set; }
    public string Division { get; set; } = "";
    public int Rank { get; set; }
    public string? RankChange { get; set; }
    public string FighterSlug { get; set; } = "";
    public string? FighterName { get; set; }
    public int? FighterId { get; set; }
    public DateTime FetchedAt { get; set; }
}

static class UfcComModel
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        // UFC.com's ids, not ones the database makes up.
        modelBuilder.Entity<UfcEvent>().Property(e => e.Id).ValueGeneratedNever();
        modelBuilder.Entity<UfcFight>().Property(f => f.Id).ValueGeneratedNever();
        modelBuilder.Entity<UfcFighter>().Property(f => f.Id).ValueGeneratedNever();
        modelBuilder.Entity<UfcFightAction>().Property(a => a.Id).ValueGeneratedNever();

        modelBuilder.Entity<UfcFight>()
            .HasOne<UfcEvent>().WithMany().HasForeignKey(f => f.EventId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<UfcFight>()
            .HasOne<UfcFighter>().WithMany().HasForeignKey(f => f.WinnerFighterId).OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<UfcFightFighter>().HasKey(f => new { f.FightId, f.FighterId });
        modelBuilder.Entity<UfcFightStats>().HasKey(s => new { s.FightId, s.FighterId });
        modelBuilder.Entity<UfcRoundStats>().HasKey(s => new { s.FightId, s.FighterId, s.Round });
        modelBuilder.Entity<UfcFightScore>().HasKey(s => new { s.FightId, s.JudgeId, s.FighterId });
        modelBuilder.Entity<UfcRoundScore>().HasKey(s => new { s.FightId, s.JudgeId, s.Round, s.FighterId });
        modelBuilder.Entity<UfcFighterProfile>().HasKey(p => p.FighterId);

        ConfigureFightChild<UfcFightFighter>(modelBuilder, x => x.FightId, x => x.FighterId);
        ConfigureFightChild<UfcFightStats>(modelBuilder, x => x.FightId, x => x.FighterId);
        ConfigureFightChild<UfcRoundStats>(modelBuilder, x => x.FightId, x => x.FighterId);
        ConfigureFightChild<UfcFightScore>(modelBuilder, x => x.FightId, x => x.FighterId);
        ConfigureFightChild<UfcRoundScore>(modelBuilder, x => x.FightId, x => x.FighterId);

        modelBuilder.Entity<UfcFightAction>()
            .HasOne<UfcFight>().WithMany().HasForeignKey(a => a.FightId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<UfcFightAction>()
            .HasOne<UfcFighter>().WithMany().HasForeignKey(a => a.FighterId).OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<UfcFighterProfile>()
            .HasOne<UfcFighter>().WithOne().HasForeignKey<UfcFighterProfile>(p => p.FighterId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<UfcRanking>()
            .HasOne<UfcFighter>().WithMany().HasForeignKey(r => r.FighterId).OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<UfcFighter>().HasIndex(f => f.Slug);
    }

    static void ConfigureFightChild<T>(ModelBuilder modelBuilder,
        System.Linq.Expressions.Expression<Func<T, object?>> fightId,
        System.Linq.Expressions.Expression<Func<T, object?>> fighterId) where T : class
    {
        modelBuilder.Entity<T>().HasOne<UfcFight>().WithMany().HasForeignKey(fightId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<T>().HasOne<UfcFighter>().WithMany().HasForeignKey(fighterId).OnDelete(DeleteBehavior.Cascade);
    }
}
