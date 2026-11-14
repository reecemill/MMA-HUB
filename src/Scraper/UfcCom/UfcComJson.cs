using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

// The shape of the UFC.com stats API's JSON (see UfcComClient for where it lives):
//   event/live/{EventId}.json  the card, results, judges, fighters, play-by-play
//   fight/live/{FightId}.json  one fight again, plus its stats in total and by round
// The API's keys are already PascalCase, so properties map by name with no attributes.
// Only the fields the scraper uses are here; the deserializer skips the rest.

static class UfcComJson
{
    public static LiveEvent? ParseEvent(string json) =>
        JsonSerializer.Deserialize<LiveEventResponse>(json)?.LiveEventDetail;

    public static LiveFight? ParseFight(string json) =>
        JsonSerializer.Deserialize<LiveFightResponse>(json)?.LiveFightDetail;
}

class LiveEventResponse
{
    public LiveEvent? LiveEventDetail { get; set; }
}

class LiveFightResponse
{
    public LiveFight? LiveFightDetail { get; set; }
}

// An ID that has no event yet comes back as an empty object: {"LiveEventDetail":{}}.
class LiveEvent
{
    public int EventId { get; set; }
    public string? Name { get; set; }
    public DateTime? StartTime { get; set; }
    public string? TimeZone { get; set; }
    public string? Status { get; set; }
    public LiveOrganization? Organization { get; set; }
    public LiveLocation? Location { get; set; }
    public List<LiveFight>? FightCard { get; set; }

    public bool IsEmpty => EventId == 0;
    public bool IsUfc => Organization?.OrganizationId == LiveOrganization.Ufc;

    // Some 1990s events and fights are "Over" rather than "Final", with results all the same.
    public bool IsFinished => Status is "Final" or "Over";
    public bool IsCanceled => Status == "Canceled";
}

class LiveOrganization
{
    // The API also has Cage Warriors, Dana White's Contender Series, and others.
    public const int Ufc = 1;

    public int OrganizationId { get; set; }
    public string? Name { get; set; }
}

class LiveLocation
{
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? TriCode { get; set; }
    public int? VenueId { get; set; }
    public string? Venue { get; set; }
}

class LiveFight
{
    public int FightId { get; set; }
    public int? FightOrder { get; set; }
    public string? Status { get; set; }
    public string? CardSegment { get; set; }
    public DateTime? CardSegmentStartTime { get; set; }
    public string? CardSegmentBroadcaster { get; set; }
    public List<LiveFighter>? Fighters { get; set; }
    public LiveResult? Result { get; set; }
    public LiveWeightClass? WeightClass { get; set; }
    public List<LiveAccolade>? Accolades { get; set; }
    public LiveReferee? Referee { get; set; }
    public LiveRuleSet? RuleSet { get; set; }

    // Only in the event JSON.
    public List<LiveAction>? FightNightTracking { get; set; }

    // Only in the fight JSON.
    public bool? OfficialStats { get; set; }
    public List<UfcFightStats>? FightStats { get; set; }
    public List<LiveFighterRounds>? RoundStats { get; set; }

    public bool IsFinished => Status is "Final" or "Over";
}

class LiveFighter
{
    public int FighterId { get; set; }
    public int? MMAId { get; set; }
    public LiveName? Name { get; set; }
    public LivePlace? Born { get; set; }
    public LivePlace? FightingOutOf { get; set; }

    // The fighter's record now, not on the night: UFC 92 (2008) lists Rashad Evans
    // at 19-8-1, his record when he retired.
    public LiveRecord? Record { get; set; }

    [JsonConverter(typeof(LenientDateOnlyConverter))]
    public DateOnly? DOB { get; set; }
    public string? Stance { get; set; }
    public decimal? Weight { get; set; }
    public decimal? Height { get; set; }
    public decimal? Reach { get; set; }
    public string? UFCLink { get; set; }
    public List<LiveWeightClass>? WeightClasses { get; set; }
    public string? Corner { get; set; }
    public decimal? WeighIn { get; set; }
    public LiveOutcome? Outcome { get; set; }
    public bool KOOfTheNight { get; set; }
    public bool SubmissionOfTheNight { get; set; }
    public bool PerformanceOfTheNight { get; set; }
}

class LiveName
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? NickName { get; set; }
}

class LivePlace
{
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
}

class LiveRecord
{
    public int? Wins { get; set; }
    public int? Losses { get; set; }
    public int? Draws { get; set; }
    public int? NoContests { get; set; }
}

class LiveOutcome
{
    public string? Outcome { get; set; }
}

class LiveWeightClass
{
    public int? WeightClassId { get; set; }

    // On a fighter: 1 is their main weight class.
    public int? WeightClassOrder { get; set; }
    public string? Description { get; set; }
    public string? Weight { get; set; }
}

class LiveAccolade
{
    public string? Type { get; set; }
    public string? Name { get; set; }
}

class LiveReferee
{
    public int? RefereeId { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

class LiveRuleSet
{
    public int? PossibleRounds { get; set; }
    public string? Description { get; set; }
}

class LiveResult
{
    public string? Method { get; set; }
    public int? EndingRound { get; set; }
    public string? EndingTime { get; set; }
    public string? EndingStrike { get; set; }
    public string? EndingTarget { get; set; }
    public string? EndingPosition { get; set; }
    public string? EndingSubmission { get; set; }
    public string? EndingNotes { get; set; }
    public bool FightOfTheNight { get; set; }
    public List<LiveJudgeTotal>? FightScores { get; set; }
    public List<LiveJudgeRounds>? RoundScores { get; set; }
}

class LiveJudgeTotal
{
    public int JudgeId { get; set; }
    public string? JudgeFirstName { get; set; }
    public string? JudgeLastName { get; set; }
    public List<LiveFighterScore>? Fighters { get; set; }
}

class LiveJudgeRounds
{
    public int JudgeId { get; set; }
    public string? JudgeFirstName { get; set; }
    public string? JudgeLastName { get; set; }
    public List<LiveRoundScore>? Rounds { get; set; }
}

class LiveRoundScore
{
    public int RoundNumber { get; set; }
    public List<LiveFighterScore>? Fighters { get; set; }
}

class LiveFighterScore
{
    public int FighterId { get; set; }
    public int? Score { get; set; }
}

class LiveFighterRounds
{
    public int FighterId { get; set; }
    public List<UfcRoundStats>? Rounds { get; set; }
}

// One entry in a fight's play-by-play: walkouts, round starts, takedowns, knockdowns,
// submission attempts, pauses, and so on.
class LiveAction
{
    public int ActionId { get; set; }
    public int? FighterId { get; set; }
    public string? Type { get; set; }
    public int? RoundNumber { get; set; }
    public string? RoundTime { get; set; }
    public DateTime? Timestamp { get; set; }
}

// "4:48" -> 288. Stat times come as minutes:seconds strings; null or "--" -> 0.
class ClockSecondsConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => UfcComText.ClockSeconds(reader.GetString()) ?? 0,
            JsonTokenType.Number => reader.GetInt32(),
            _ => 0
        };

    public override bool HandleNull => true;

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) =>
        writer.WriteStringValue($"{value / 60}:{value % 60:00}");
}

// Dates of birth are "1996-01-03"; an empty or unreadable one becomes null instead of
// failing the whole file.
class LenientDateOnlyConverter : JsonConverter<DateOnly?>
{
    public override DateOnly? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String &&
        DateOnly.TryParseExact(reader.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date)
            ? date : null;

    public override void Write(Utf8JsonWriter writer, DateOnly? value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value?.ToString("yyyy-MM-dd"));
}
