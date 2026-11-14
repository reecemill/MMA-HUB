namespace Web.Models;

// The scraper's UFC.com tables (src/Scraper/UfcCom/UfcComEntities.cs), as the site reads
// them. Only the columns the site, the snapshot, or the model use are mapped here.
public class Event
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DateTime? StartTime { get; set; }
    // The date where the event took place; StartTime is in UTC.
    public DateOnly? Date { get; set; }
    public string? Status { get; set; }
    public string? Venue { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public List<Fight> Fights { get; set; } = [];

    public bool IsUpcoming => Status == "Upcoming";

    // UFC.com marks a few 1990s events "Over" rather than "Final".
    public string StatusText => Status switch
    {
        "Final" or "Over" => "Completed",
        null or "" => Format.Missing,
        _ => Status,
    };

    public string? Location => Format.Join(City, State, Country);
}

public class Fight
{
    public int Id { get; set; }
    public int EventId { get; set; }
    public Event? Event { get; set; }
    public int? FightOrder { get; set; }
    public string? Status { get; set; }
    public string? CardSegment { get; set; }
    public string? WeightClass { get; set; }
    public bool IsTitleFight { get; set; }
    public int? ScheduledRounds { get; set; }
    public string? Referee { get; set; }
    public int? WinnerFighterId { get; set; }
    public string? Method { get; set; }
    public int? EndingRound { get; set; }
    public string? EndingTime { get; set; }
    public int? DurationSeconds { get; set; }
    public string? EndingStrike { get; set; }
    public string? EndingTarget { get; set; }
    public string? EndingPosition { get; set; }
    public string? EndingSubmission { get; set; }
    public bool FightOfTheNight { get; set; }

    public List<FightFighter> Fighters { get; set; } = [];
    public List<FightStats> Stats { get; set; } = [];
    public List<FightScore> Scores { get; set; } = [];

    public bool IsFinished => Status is "Final" or "Over";

    // UFC.com lists the red corner first: the champion, or the higher-billed fighter.
    public FightFighter? Red => Fighters.FirstOrDefault(f => f.Corner == "Red");
    public FightFighter? Blue => Fighters.FirstOrDefault(f => f.Corner == "Blue");

    public string CardSection => CardSegment switch
    {
        "Main" => "Main Card",
        "Prelims1" => "Prelims",
        "Prelims2" => "Early Prelims",
        _ => "Card",
    };

    public int CardSectionOrder => CardSegment switch { "Main" => 0, "Prelims1" => 1, "Prelims2" => 2, _ => 3 };

    // "KO/TKO (R2)"
    public string Result => (Method ?? Format.Missing) + (EndingRound != null ? $" (R{EndingRound})" : "");

    // How a finish happened: "Punches to the head, from mount" or "Rear Naked Choke, from back control".
    public string? Ending
    {
        get
        {
            string? position = EndingPosition?.ToLowerInvariant();
            string? what = EndingSubmission
                ?? (EndingStrike != null ? EndingStrike + (EndingTarget != null ? $" to the {EndingTarget.ToLowerInvariant()}" : "") : null);
            return what == null ? null : position == null ? what : $"{what}, {position}";
        }
    }

    public FightStats? StatsFor(int? fighterId) => Stats.FirstOrDefault(s => s.FighterId == fighterId);
}

public class FightFighter
{
    public int FightId { get; set; }
    public Fight? Fight { get; set; }
    public int FighterId { get; set; }
    public Fighter? Fighter { get; set; }
    public string? Corner { get; set; }
    // "Win", "Loss", "Draw", or "No Contest"; null until the fight happens.
    public string? Outcome { get; set; }
}

// One fighter's numbers for a whole fight. Times are in seconds.
public class FightStats
{
    public int FightId { get; set; }
    public int FighterId { get; set; }
    public int Knockdowns { get; set; }
    public int TotalStrikesLanded { get; set; }
    public int TotalStrikesAttempted { get; set; }
    public int SigStrikesLanded { get; set; }
    public int SigStrikesAttempted { get; set; }
    public int SigHeadStrikesLanded { get; set; }
    public int SigHeadStrikesAttempted { get; set; }
    public int SigBodyStrikesLanded { get; set; }
    public int SigBodyStrikesAttempted { get; set; }
    public int SigLegStrikesLanded { get; set; }
    public int SigLegStrikesAttempted { get; set; }
    public int SigDistanceStrikesLanded { get; set; }
    public int SigDistanceStrikesAttempted { get; set; }
    public int SigClinchStrikesLanded { get; set; }
    public int SigClinchStrikesAttempted { get; set; }
    public int SigGroundStrikesLanded { get; set; }
    public int SigGroundStrikesAttempted { get; set; }
    public int TakedownsLanded { get; set; }
    public int TakedownsAttempted { get; set; }
    public int SubmissionsAttempted { get; set; }
    public int Reversals { get; set; }
    public int ControlSeconds { get; set; }
}

// A judge's total for one fighter, on fights that went to the scorecards.
public class FightScore
{
    public int FightId { get; set; }
    public int JudgeId { get; set; }
    public int FighterId { get; set; }
    public string? JudgeName { get; set; }
    public int? Score { get; set; }
}
