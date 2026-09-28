using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

IConfiguration config = new ConfigurationBuilder()
    .AddUserSecrets<Program>()
    .Build();

if (args.Contains("--dedupe-only"))
{
    EventDeduplicator.Run(config["ConnectionStrings:MmaDb"]!);
    return;
}

string? apiKey = config["CitoApi:ApiKey"];

var client = new HttpClient();
client.DefaultRequestHeaders.Add("x-api-key", apiKey);

async Task<string?> FetchWithRetry(string url)
{
    for (int attempt = 1; attempt <= 3; attempt++)
    {
        await Task.Delay(1500);
        var response = await client.GetAsync(url);
        if ((int)response.StatusCode == 429)
        {
            Console.WriteLine("Rate limited — API quota may be exhausted. Try again later.");
            return null;
        }
        if (response.IsSuccessStatusCode)
            return await response.Content.ReadAsStringAsync();
        if (attempt < 3)
            await Task.Delay(5000);
    }
    Console.WriteLine($"Failed after 3 attempts: {url}");
    return null;
}

// Career stats come back from the API as strings (e.g. "0.59"), not JSON numbers.
decimal ParseDecimal(string? value) => decimal.TryParse(value, out decimal result) ? result : 0;

// Bout stat fields come back as "136 of 273" (landed of attempted).
(int Landed, int Attempted) ParseLandedOfAttempted(string? value)
{
    if (string.IsNullOrEmpty(value)) return (0, 0);
    string[] parts = value.Split(" of ");
    if (parts.Length != 2) return (0, 0);
    return (int.TryParse(parts[0], out int landed) ? landed : 0, int.TryParse(parts[1], out int attempted) ? attempted : 0);
}

// Cito's event dates are frequently wrong by 1-2 years for events that have a dated
// promo image (confirmed by comparing startsAt against the real date baked into the
// image filename, e.g. ".../100524-ufc-307-...jpg" = Oct 5 2024, while the API's own
// startsAt said Oct 2026). Events with no image weren't affected, so only override
// when we can actually extract a date from the filename.
DateTime? TryGetCorrectedEventDate(string? imageUrl)
{
    if (string.IsNullOrEmpty(imageUrl)) return null;

    Match match = Regex.Match(imageUrl, @"(\d{2})(\d{2})(\d{2})-ufc-", RegexOptions.IgnoreCase);
    if (!match.Success) return null;

    int month = int.Parse(match.Groups[1].Value);
    int day = int.Parse(match.Groups[2].Value);
    int year = 2000 + int.Parse(match.Groups[3].Value);

    try
    {
        return new DateTime(year, month, day);
    }
    catch (ArgumentOutOfRangeException)
    {
        return null;
    }
}

// Cito falls back to a broken "Search results" name (scraped from a UFC.com search
// page) when it can't resolve a fighter's real athlete page — mainly older/retired
// fighters. The slug is unaffected, so derive a readable name from it instead.
string? DeriveNameFromSlug(string? slug)
{
    if (string.IsNullOrEmpty(slug)) return null;
    return string.Join(' ', slug.Split('-').Select(part =>
        part.Length > 0 ? char.ToUpper(part[0]) + part[1..] : part));
}

string firstPageUrl = "https://api.citoapi.com/api/v1/ufc/fighters?page=1&limit=50";
string? firstPageJson = await FetchWithRetry(firstPageUrl);
if (firstPageJson == null) return;
PageInfo? firstPage = JsonSerializer.Deserialize<PageInfo>(firstPageJson);
int totalPages = firstPage?.Meta?.TotalPages ?? 1;

List<Fighter> allFighters = new List<Fighter>();

for (int pageNumber = 1; pageNumber <= totalPages; pageNumber++)
{
    string url = $"https://api.citoapi.com/api/v1/ufc/fighters?page={pageNumber}&limit=50";
    string? pageJson = await FetchWithRetry(url);
    if (pageJson == null) break;
    PageInfo? page = JsonSerializer.Deserialize<PageInfo>(pageJson);

    if (page?.Data != null)
    {
        allFighters.AddRange(page.Data);
    }
}

Console.WriteLine(allFighters.Count);

// Must run before the duplicate-stub check below: while names are still the broken
// "Search results" fallback, every affected fighter shares one identical name, which
// would make the name-based duplicate match below fire on totally unrelated people.
foreach (Fighter fighter in allFighters)
{
    if (fighter.Name == "Search results")
    {
        fighter.Name = DeriveNameFromSlug(fighter.Slug) ?? fighter.Name;
    }
}

// Cito sometimes issues a new Id/slug for a person it already has a record for,
// producing two rows for the same real fighter: one fully populated (has a
// UfcStatsId), one an empty stub (no UfcStatsId, no stats). Skip the stub rather
// than auto-merging — an automatic same-name merge risks colliding two different
// people who happen to share a name.
HashSet<Guid> duplicateStubIds = allFighters
    .Where(f => string.IsNullOrEmpty(f.UfcStatsId))
    .Join(allFighters.Where(f => !string.IsNullOrEmpty(f.UfcStatsId)),
        stub => stub.Name, twin => twin.Name, (stub, twin) => stub.Id)
    .ToHashSet();

foreach (Guid id in duplicateStubIds)
{
    Fighter stub = allFighters.First(f => f.Id == id);
    Console.WriteLine($"Skipping duplicate stub fighter \"{stub.Name}\" (slug '{stub.Slug}') — a populated record exists under a different slug.");
}

allFighters = allFighters.Where(f => !duplicateStubIds.Contains(f.Id)).ToList();

foreach (Fighter fighter in allFighters)
{
    if (fighter.Raw?.HeroStats != null)
    {
        foreach (KeyValuePair<string, string> stat in fighter.Raw.HeroStats)
        {
            fighter.HeroStatRows.Add(new FighterHeroStat
            {
                FighterId = fighter.Id,
                StatKey = stat.Key,
                StatValue = stat.Value
            });
        }
    }

    fighter.RawJson = JsonSerializer.Serialize(fighter.Raw);
}

string dbConnectionString = config["ConnectionStrings:MmaDb"]!;

// Career stats (striking/takedown numbers) and division rankings both only come back
// from the per-fighter detail endpoint, not the bulk list — same one-request-per-fighter
// pattern as the event venue/location backfill below. Fetched together here (one request
// covers both) before Fighters are saved, so Fighter.Rank lands in the same save as
// everything else instead of needing a separate update pass.
List<FighterStats> allFighterStats = new List<FighterStats>();
int statsFetched = 0;
foreach (Fighter fighter in allFighters)
{
    if (string.IsNullOrEmpty(fighter.Slug)) continue;

    string statsDetailUrl = $"https://api.citoapi.com/api/v1/ufc/fighters/{fighter.Slug}";
    string? statsDetailJson = await FetchWithRetry(statsDetailUrl);
    if (statsDetailJson == null) continue;

    FighterDetailResponse? statsDetail = JsonSerializer.Deserialize<FighterDetailResponse>(statsDetailJson);

    // The UFC's own ("meta") ranking entry that matches this fighter's own division.
    // Champions show up with rank == null (rankText "C") since ChampionStatus already
    // covers that case; the P4P-only entry has a division that won't match theirs.
    RankingEntry? divisionRanking = statsDetail?.Data?.RankingsRaw?
        .FirstOrDefault(r => r.System == "meta" && string.Equals(r.Division, fighter.Division, StringComparison.OrdinalIgnoreCase));
    fighter.Rank = divisionRanking?.Rank;

    FighterStatsRaw? raw = statsDetail?.Data?.StatsRaw;
    if (raw == null) continue;

    allFighterStats.Add(new FighterStats
    {
        FighterId = fighter.Id,
        SigStrikesLanded = raw.SignificantStrikesLanded ?? 0,
        SigStrikesAttempted = raw.SignificantStrikesAttempted ?? 0,
        StrikingAccuracy = ParseDecimal(raw.StrikingAccuracy),
        SigStrikesLandedPerMin = ParseDecimal(raw.SigStrikesLandedPerMin),
        SigStrikesAbsorbedPerMin = ParseDecimal(raw.SigStrikesAbsorbedPerMin),
        SigStrikesDefense = ParseDecimal(raw.SigStrikeDefense),
        TakedownsLanded = raw.TakedownsLanded ?? 0,
        TakedownsAttempted = raw.TakedownsAttempted ?? 0,
        TakedownAccuracy = ParseDecimal(raw.TakedownAccuracy),
        TakedownAvgPer15Min = ParseDecimal(raw.TakedownAvgPer15Min),
        TakedownDefense = ParseDecimal(raw.TakedownDefense),
        SubmissionAvgPer15Min = ParseDecimal(raw.SubmissionAvgPer15Min),
        KnockdownAvg = ParseDecimal(raw.KnockdownAvg),
        AverageFightTimeSeconds = raw.AverageFightTimeSeconds ?? 0,
        StandingStrikePercent = raw.SigStrikesByPosition?.Standing?.Percent ?? 0,
        ClinchStrikePercent = raw.SigStrikesByPosition?.Clinch?.Percent ?? 0,
        GroundStrikePercent = raw.SigStrikesByPosition?.Ground?.Percent ?? 0,
        HeadStrikePercent = raw.SigStrikesByTarget?.Head?.Percent ?? 0,
        BodyStrikePercent = raw.SigStrikesByTarget?.Body?.Percent ?? 0,
        LegStrikePercent = raw.SigStrikesByTarget?.Leg?.Percent ?? 0
    });

    statsFetched++;
    if (statsFetched % 50 == 0)
    {
        Console.WriteLine($"Fetched career stats for {statsFetched} of {allFighters.Count} fighters...");
    }
}

Console.WriteLine($"Fetched career stats for {statsFetched} fighters.");

using (AppDbContext db = new AppDbContext(dbConnectionString))
{
    foreach (Fighter fighter in allFighters)
    {
        Fighter? existing = db.Fighters.Find(fighter.Id);

        if (existing == null)
        {
            db.Fighters.Add(fighter);
        }
        else
        {
            db.Entry(existing).CurrentValues.SetValues(fighter);
        }
    }

    db.SaveChanges();
}

Console.WriteLine("Saved " + allFighters.Count + " fighters to the database.");

using (AppDbContext db = new AppDbContext(dbConnectionString))
{
    foreach (FighterStats stats in allFighterStats)
    {
        FighterStats? existing = db.FighterStats.Find(stats.FighterId);

        if (existing == null)
        {
            db.FighterStats.Add(stats);
        }
        else
        {
            db.Entry(existing).CurrentValues.SetValues(stats);
        }
    }

    db.SaveChanges();
}

Console.WriteLine("Saved " + allFighterStats.Count + " fighter stats records to the database.");

string firstEventPageUrl = "https://api.citoapi.com/api/v1/ufc/events?page=1&limit=50";
string? firstEventPageJson = await FetchWithRetry(firstEventPageUrl);
if (firstEventPageJson == null) return;
EventPageInfo? firstEventPage = JsonSerializer.Deserialize<EventPageInfo>(firstEventPageJson);
int eventTotalPages = firstEventPage?.Meta?.TotalPages ?? 1;

List<Event> allEvents = new List<Event>();

for (int pageNumber = 1; pageNumber <= eventTotalPages; pageNumber++)
{
    string url = $"https://api.citoapi.com/api/v1/ufc/events?page={pageNumber}&limit=50";
    string? pageJson = await FetchWithRetry(url);
    if (pageJson == null) break;
    EventPageInfo? page = JsonSerializer.Deserialize<EventPageInfo>(pageJson);

    if (page?.Data != null)
    {
        foreach (Event ev in page.Data)
        {
            var raw = new Dictionary<string, object?>();
            if (ev.BroadcastInfo.HasValue) raw["broadcastInfo"] = ev.BroadcastInfo;
            if (ev.DataAvailability.HasValue) raw["dataAvailability"] = ev.DataAvailability;
            if (ev.ExtraData != null)
                foreach (var kv in ev.ExtraData) raw[kv.Key] = kv.Value;

            ev.RawJson = raw.Count > 0 ? JsonSerializer.Serialize(raw) : null;

            DateTime? correctedDate = TryGetCorrectedEventDate(ev.ImageUrl);
            if (correctedDate.HasValue)
            {
                ev.EventDate = correctedDate;
                ev.StartsAt = correctedDate;
            }
        }

        allEvents.AddRange(page.Data);
    }
}

Console.WriteLine(allEvents.Count);

// The bulk list endpoint returns venue/city/country blank for most events, but the
// individual event-detail endpoint has the real values — so backfill each event with
// one extra request. This is the slow part: one request per event, same rate limiting
// as everything else. The same response also carries the fight card (bouts), so that's
// parsed here too rather than costing a second request per event.
Dictionary<string, Guid> fighterIdBySlug = allFighters
    .Where(f => !string.IsNullOrEmpty(f.Slug))
    .GroupBy(f => f.Slug!, StringComparer.OrdinalIgnoreCase)
    .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

List<Bout> allBouts = new List<Bout>();
List<BoutFighterStats> allBoutFighterStats = new List<BoutFighterStats>();
int backfilled = 0;
foreach (Event ev in allEvents)
{
    if (string.IsNullOrEmpty(ev.Slug)) continue;

    string detailUrl = $"https://api.citoapi.com/api/v1/ufc/events/{ev.Slug}";
    string? detailJson = await FetchWithRetry(detailUrl);
    if (detailJson == null) continue;

    EventDetailResponse? detail = JsonSerializer.Deserialize<EventDetailResponse>(detailJson);
    if (detail?.Data == null) continue;

    ev.Venue = detail.Data.Venue ?? ev.Venue;
    ev.City = detail.Data.City ?? ev.City;
    ev.State = detail.Data.State ?? ev.State;
    ev.Country = detail.Data.Country ?? ev.Country;
    ev.LocationText = detail.Data.LocationText ?? ev.LocationText;

    if (detail.Data.BoutsRaw != null)
    {
        Guid? ResolveFighterId(string? slug) =>
            slug != null && fighterIdBySlug.TryGetValue(slug, out Guid id) ? id : null;

        string FighterPairKey(BoutRaw raw) => string.Join("|",
            (raw.Fighters ?? []).Select(f => f.FighterSlug).OrderBy(s => s, StringComparer.OrdinalIgnoreCase));

        // CitoAPI returns two entries for the same fight once ufcstats.com enrichment
        // is available: a primary entry (no boutStats) and a stats-only duplicate with
        // a different id but the same two fighters. Group by fighter pair so the stats
        // land on one real Bout row instead of creating a second row for the same fight.
        var boutsByFighterPair = detail.Data.BoutsRaw
            .Where(b => !string.IsNullOrEmpty(b.Id))
            .GroupBy(FighterPairKey);

        foreach (var group in boutsByFighterPair)
        {
            BoutRaw primary = group.FirstOrDefault(b => b.BoutStats == null || b.BoutStats.Count == 0) ?? group.First();

            BoutFighterRaw? fighter1 = primary.Fighters?.ElementAtOrDefault(0);
            BoutFighterRaw? fighter2 = primary.Fighters?.ElementAtOrDefault(1);

            allBouts.Add(new Bout
            {
                Id = primary.Id!,
                EventId = ev.Id,
                CardSection = primary.CardSection,
                CardSectionOrder = primary.CardSectionOrder,
                BoutOrder = primary.BoutOrder,
                WeightClass = primary.WeightClass,
                TitleBout = primary.TitleBout,
                Status = primary.Status,
                IsCancelled = primary.IsCancelled,
                ResultRound = primary.ResultRound,
                ResultTime = primary.ResultTime,
                Method = primary.Method,
                Fighter1Id = ResolveFighterId(fighter1?.FighterSlug),
                Fighter1Slug = fighter1?.FighterSlug,
                Fighter1Name = fighter1?.FighterName,
                Fighter1Corner = fighter1?.Corner,
                Fighter1Outcome = fighter1?.Outcome,
                Fighter2Id = ResolveFighterId(fighter2?.FighterSlug),
                Fighter2Slug = fighter2?.FighterSlug,
                Fighter2Name = fighter2?.FighterName,
                Fighter2Corner = fighter2?.Corner,
                Fighter2Outcome = fighter2?.Outcome,
                WinnerFighterId = ResolveFighterId(primary.WinnerFighterSlug)
            });

            BoutRaw? statsEntry = group.FirstOrDefault(b => b.BoutStats is { Count: > 0 });
            if (statsEntry?.BoutStats != null)
            {
                foreach (BoutStatRaw stat in statsEntry.BoutStats)
                {
                    if (string.IsNullOrEmpty(stat.Id)) continue;

                    var (sigLanded, sigAttempted) = ParseLandedOfAttempted(stat.SignificantStrikes);
                    var (totalLanded, totalAttempted) = ParseLandedOfAttempted(stat.TotalStrikes);
                    var (tdLanded, tdAttempted) = ParseLandedOfAttempted(stat.Takedowns);
                    var (headLanded, headAttempted) = ParseLandedOfAttempted(stat.Head);
                    var (bodyLanded, bodyAttempted) = ParseLandedOfAttempted(stat.Body);
                    var (legLanded, legAttempted) = ParseLandedOfAttempted(stat.Leg);
                    var (distanceLanded, distanceAttempted) = ParseLandedOfAttempted(stat.Distance);
                    var (clinchLanded, clinchAttempted) = ParseLandedOfAttempted(stat.Clinch);
                    var (groundLanded, groundAttempted) = ParseLandedOfAttempted(stat.Ground);

                    allBoutFighterStats.Add(new BoutFighterStats
                    {
                        Id = stat.Id,
                        BoutId = primary.Id!,
                        FighterId = ResolveFighterId(stat.FighterSlug),
                        FighterSlug = stat.FighterSlug,
                        Knockdowns = stat.Knockdowns,
                        SigStrikesLanded = sigLanded,
                        SigStrikesAttempted = sigAttempted,
                        TotalStrikesLanded = totalLanded,
                        TotalStrikesAttempted = totalAttempted,
                        TakedownsLanded = tdLanded,
                        TakedownsAttempted = tdAttempted,
                        SubmissionAttempts = stat.SubmissionAttempts,
                        Reversals = stat.Reversals,
                        ControlTime = stat.ControlTime,
                        HeadLanded = headLanded,
                        HeadAttempted = headAttempted,
                        BodyLanded = bodyLanded,
                        BodyAttempted = bodyAttempted,
                        LegLanded = legLanded,
                        LegAttempted = legAttempted,
                        DistanceLanded = distanceLanded,
                        DistanceAttempted = distanceAttempted,
                        ClinchLanded = clinchLanded,
                        ClinchAttempted = clinchAttempted,
                        GroundLanded = groundLanded,
                        GroundAttempted = groundAttempted
                    });
                }
            }
        }
    }

    backfilled++;
    if (backfilled % 50 == 0)
    {
        Console.WriteLine($"Backfilled venue/location for {backfilled} of {allEvents.Count} events...");
    }
}

Console.WriteLine($"Finished backfilling venue/location for {backfilled} events.");
Console.WriteLine($"Parsed {allBouts.Count} bouts.");

// Sanity check: numbered UFC events should always land in date order matching their
// number (UFC 312 should never be dated after UFC 313). This won't fix bad data by
// itself, but it makes sure new anomalies get flagged every run instead of only being
// found by manually re-running SQL checks.
var numberedEvents = allEvents
    .Where(e => e.EventDate.HasValue && e.Title != null && Regex.IsMatch(e.Title, @"^UFC \d+$"))
    .Select(e => new { e.Title, Date = e.EventDate!.Value, Number = int.Parse(Regex.Match(e.Title!, @"\d+").Value) })
    .OrderBy(e => e.Date)
    .ToList();

for (int i = 1; i < numberedEvents.Count; i++)
{
    if (numberedEvents[i].Number < numberedEvents[i - 1].Number)
    {
        Console.WriteLine(
            $"Warning: {numberedEvents[i].Title} ({numberedEvents[i].Date:yyyy-MM-dd}) is dated before " +
            $"{numberedEvents[i - 1].Title} ({numberedEvents[i - 1].Date:yyyy-MM-dd}) despite having a higher number.");
    }
}

using (AppDbContext db = new AppDbContext(dbConnectionString))
{
    foreach (Event ev in allEvents)
    {
        Event? existing = db.Events.Find(ev.Id);

        if (existing == null)
        {
            db.Events.Add(ev);
        }
        else
        {
            db.Entry(existing).CurrentValues.SetValues(ev);
        }
    }

    db.SaveChanges();
}

Console.WriteLine("Saved " + allEvents.Count + " events to the database.");

using (AppDbContext db = new AppDbContext(dbConnectionString))
{
    foreach (Bout bout in allBouts)
    {
        Bout? existing = db.Bouts.Find(bout.Id);

        if (existing == null)
        {
            db.Bouts.Add(bout);
        }
        else
        {
            db.Entry(existing).CurrentValues.SetValues(bout);
        }
    }

    db.SaveChanges();
}

Console.WriteLine("Saved " + allBouts.Count + " bouts to the database.");

using (AppDbContext db = new AppDbContext(dbConnectionString))
{
    foreach (BoutFighterStats stat in allBoutFighterStats)
    {
        BoutFighterStats? existing = db.BoutFighterStats.Find(stat.Id);

        if (existing == null)
        {
            db.BoutFighterStats.Add(stat);
        }
        else
        {
            db.Entry(existing).CurrentValues.SetValues(stat);
        }
    }

    db.SaveChanges();
}

Console.WriteLine("Saved " + allBoutFighterStats.Count + " bout fighter stats to the database.");

EventDeduplicator.Run(dbConnectionString);


class EventPageInfo
{
    [JsonPropertyName("meta")]
    public MetaInfo? Meta { get; set; }

    [JsonPropertyName("data")]
    public List<Event>? Data { get; set; }
}

class EventDetailResponse
{
    [JsonPropertyName("data")]
    public Event? Data { get; set; }
}

class FighterDetailResponse
{
    [JsonPropertyName("data")]
    public Fighter? Data { get; set; }
}

class PageInfo
{
    [JsonPropertyName("meta")]
    public MetaInfo? Meta { get; set; }

    [JsonPropertyName("data")]
    public List<Fighter>? Data { get; set; }
}

class MetaInfo
{
    [JsonPropertyName("totalPages")]
    public int TotalPages { get; set; }
}

class Fighter
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("slug")]
    public string? Slug { get; set; }

    [JsonPropertyName("ufcStatsId")]
    public string? UfcStatsId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("firstName")]
    public string? FirstName { get; set; }

    [JsonPropertyName("lastName")]
    public string? LastName { get; set; }

    [JsonPropertyName("nickname")]
    public string? Nickname { get; set; }

    [JsonPropertyName("division")]
    public string? Division { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }

    [JsonPropertyName("championStatus")]
    public string? ChampionStatus { get; set; }

    [JsonPropertyName("p4pRank")]
    public int? P4pRank { get; set; }

    // Not JSON-mapped directly — derived from RankingsRaw during the detail-fetch loop
    // (the "meta" ranking entry matching this fighter's own Division).
    public int? Rank { get; set; }

    [JsonPropertyName("recordWins")]
    public int? RecordWins { get; set; }

    [JsonPropertyName("recordLosses")]
    public int? RecordLosses { get; set; }

    [JsonPropertyName("recordDraws")]
    public int? RecordDraws { get; set; }

    [JsonPropertyName("recordNoContest")]
    public int? RecordNoContest { get; set; }

    [JsonPropertyName("recordText")]
    public string? RecordText { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [JsonPropertyName("placeOfBirth")]
    public string? PlaceOfBirth { get; set; }

    [JsonPropertyName("trainsAt")]
    public string? TrainsAt { get; set; }

    [JsonPropertyName("fightingStyle")]
    public string? FightingStyle { get; set; }

    [JsonPropertyName("age")]
    public int? Age { get; set; }

    [JsonPropertyName("heightInches")]
    public string? HeightInches { get; set; }

    [JsonPropertyName("weightLbs")]
    public string? WeightLbs { get; set; }

    [JsonPropertyName("reachInches")]
    public string? ReachInches { get; set; }

    [JsonPropertyName("legReachInches")]
    public string? LegReachInches { get; set; }

    [JsonPropertyName("stance")]
    public string? Stance { get; set; }

    [JsonPropertyName("octagonDebut")]
    public DateTime? OctagonDebut { get; set; }

    [JsonPropertyName("profileUrl")]
    public string? ProfileUrl { get; set; }

    [JsonPropertyName("imageUrl")]
    public string? ImageUrl { get; set; }

    [JsonPropertyName("bio")]
    public string? Bio { get; set; }

    [JsonPropertyName("raw")]
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public RawData? Raw { get; set; }

    [JsonPropertyName("stats")]
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public FighterStatsRaw? StatsRaw { get; set; }

    [JsonPropertyName("rankings")]
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public List<RankingEntry>? RankingsRaw { get; set; }

    [JsonExtensionData]
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public Dictionary<string, JsonElement>? ExtraData { get; set; }

    public string? RawJson { get; set; }

    public List<FighterHeroStat> HeroStatRows { get; set; } = new List<FighterHeroStat>();

    public FighterStats? Stats { get; set; }
}

class Event
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("slug")]
    public string? Slug { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("shortTitle")]
    public string? ShortTitle { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("startsAt")]
    public DateTime? StartsAt { get; set; }

    [JsonPropertyName("venue")]
    public string? Venue { get; set; }

    [JsonPropertyName("city")]
    public string? City { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [JsonPropertyName("locationText")]
    public string? LocationText { get; set; }

    // Set by EventDeduplicator when Cito lists this event twice; not from the API.
    [JsonIgnore]
    public Guid? DuplicateOfEventId { get; set; }

    [JsonPropertyName("imageUrl")]
    public string? ImageUrl { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("lastSyncedAt")]
    public DateTime? LastSyncedAt { get; set; }

    [JsonPropertyName("dataId")]
    public string? DataId { get; set; }

    [JsonPropertyName("hasStats")]
    public bool HasStats { get; set; }

    [JsonPropertyName("dataFreshness")]
    public DateTime? DataFreshness { get; set; }

    [JsonPropertyName("freshnessStatus")]
    public string? FreshnessStatus { get; set; }

    [JsonPropertyName("dataAgeHours")]
    public double? DataAgeHours { get; set; }

    [JsonPropertyName("dataSource")]
    public string? DataSource { get; set; }

    [JsonPropertyName("warning")]
    public string? Warning { get; set; }

    [JsonPropertyName("eventDate")]
    public DateTime? EventDate { get; set; }

    [JsonPropertyName("eventDateLabel")]
    public string? EventDateLabel { get; set; }

    [JsonPropertyName("eventWeekday")]
    public string? EventWeekday { get; set; }

    [JsonPropertyName("eventTimeZone")]
    public string? EventTimeZone { get; set; }

    [JsonPropertyName("venueTimeZone")]
    public string? VenueTimeZone { get; set; }

    [JsonPropertyName("venueDate")]
    public DateTime? VenueDate { get; set; }

    [JsonPropertyName("venueDateLabel")]
    public string? VenueDateLabel { get; set; }

    [JsonPropertyName("venueWeekday")]
    public string? VenueWeekday { get; set; }

    [JsonPropertyName("broadcastInfo")]
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public JsonElement? BroadcastInfo { get; set; }

    [JsonPropertyName("dataAvailability")]
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public JsonElement? DataAvailability { get; set; }

    [JsonPropertyName("bouts")]
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public List<BoutRaw>? BoutsRaw { get; set; }

    [JsonExtensionData]
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public Dictionary<string, JsonElement>? ExtraData { get; set; }

    public string? RawJson { get; set; }
}

// CitoAPI's bout ids are plain numeric strings (e.g. "12919"), not Guids — kept as-is
// rather than minted fresh, so re-running the scraper always upserts the same rows.
// FighterXId/WinnerFighterId are resolved from the API's fighter slugs against our own
// Fighters table; the raw slug/name are kept alongside as a fallback in case a fighter
// on the card doesn't resolve (not yet synced, slug mismatch, etc).
class Bout
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

    public Guid? Fighter2Id { get; set; }
    public string? Fighter2Slug { get; set; }
    public string? Fighter2Name { get; set; }
    public string? Fighter2Corner { get; set; }
    public string? Fighter2Outcome { get; set; }

    public Guid? WinnerFighterId { get; set; }

    // Set by EventDeduplicator when the same fight is also listed on another event.
    public string? DuplicateOfBoutId { get; set; }
}

// Landed/attempted counts come from the API as a single "X of Y" string per stat —
// split into paired columns here so the DB holds real numbers, not text to re-parse.
class BoutFighterStats
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

class BoutRaw
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("cardSection")]
    public string? CardSection { get; set; }

    [JsonPropertyName("cardSectionOrder")]
    public int? CardSectionOrder { get; set; }

    [JsonPropertyName("boutOrder")]
    public int? BoutOrder { get; set; }

    [JsonPropertyName("weightClass")]
    public string? WeightClass { get; set; }

    [JsonPropertyName("titleBout")]
    public bool TitleBout { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("isCancelled")]
    public bool IsCancelled { get; set; }

    [JsonPropertyName("winnerFighterSlug")]
    public string? WinnerFighterSlug { get; set; }

    [JsonPropertyName("resultRound")]
    public int? ResultRound { get; set; }

    [JsonPropertyName("resultTime")]
    public string? ResultTime { get; set; }

    [JsonPropertyName("method")]
    public string? Method { get; set; }

    [JsonPropertyName("fighters")]
    public List<BoutFighterRaw>? Fighters { get; set; }

    // Only present on the ufcstats.com "enrichment" duplicate of a bout, not the
    // primary UFC.com-sourced entry — see the matching logic where this is consumed.
    [JsonPropertyName("boutStats")]
    public List<BoutStatRaw>? BoutStats { get; set; }
}

class BoutStatRaw
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("fighterSlug")]
    public string? FighterSlug { get; set; }

    [JsonPropertyName("knockdowns")]
    public int Knockdowns { get; set; }

    [JsonPropertyName("significantStrikes")]
    public string? SignificantStrikes { get; set; }

    [JsonPropertyName("totalStrikes")]
    public string? TotalStrikes { get; set; }

    [JsonPropertyName("takedowns")]
    public string? Takedowns { get; set; }

    [JsonPropertyName("submissionAttempts")]
    public int SubmissionAttempts { get; set; }

    [JsonPropertyName("reversals")]
    public int Reversals { get; set; }

    [JsonPropertyName("controlTime")]
    public string? ControlTime { get; set; }

    [JsonPropertyName("head")]
    public string? Head { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("leg")]
    public string? Leg { get; set; }

    [JsonPropertyName("distance")]
    public string? Distance { get; set; }

    [JsonPropertyName("clinch")]
    public string? Clinch { get; set; }

    [JsonPropertyName("ground")]
    public string? Ground { get; set; }
}

class BoutFighterRaw
{
    [JsonPropertyName("fighterSlug")]
    public string? FighterSlug { get; set; }

    [JsonPropertyName("fighterName")]
    public string? FighterName { get; set; }

    [JsonPropertyName("corner")]
    public string? Corner { get; set; }

    [JsonPropertyName("outcome")]
    public string? Outcome { get; set; }
}

class FighterHeroStat
{
    public int Id { get; set; }
    public Guid FighterId { get; set; }
    public string StatKey { get; set; } = "";
    public string StatValue { get; set; } = "";
}

class FighterStats
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

class RankingEntry
{
    [JsonPropertyName("system")]
    public string? System { get; set; }

    [JsonPropertyName("division")]
    public string? Division { get; set; }

    [JsonPropertyName("rank")]
    public int? Rank { get; set; }

    [JsonPropertyName("isChampion")]
    public bool IsChampion { get; set; }
}

class FighterStatsRaw
{
    [JsonPropertyName("strikingAccuracy")]
    public string? StrikingAccuracy { get; set; }

    [JsonPropertyName("significantStrikesLanded")]
    public int? SignificantStrikesLanded { get; set; }

    [JsonPropertyName("significantStrikesAttempted")]
    public int? SignificantStrikesAttempted { get; set; }

    [JsonPropertyName("takedownAccuracy")]
    public string? TakedownAccuracy { get; set; }

    [JsonPropertyName("takedownsLanded")]
    public int? TakedownsLanded { get; set; }

    [JsonPropertyName("takedownsAttempted")]
    public int? TakedownsAttempted { get; set; }

    [JsonPropertyName("sigStrikesLandedPerMin")]
    public string? SigStrikesLandedPerMin { get; set; }

    [JsonPropertyName("sigStrikesAbsorbedPerMin")]
    public string? SigStrikesAbsorbedPerMin { get; set; }

    [JsonPropertyName("takedownAvgPer15Min")]
    public string? TakedownAvgPer15Min { get; set; }

    [JsonPropertyName("submissionAvgPer15Min")]
    public string? SubmissionAvgPer15Min { get; set; }

    [JsonPropertyName("sigStrikeDefense")]
    public string? SigStrikeDefense { get; set; }

    [JsonPropertyName("takedownDefense")]
    public string? TakedownDefense { get; set; }

    [JsonPropertyName("knockdownAvg")]
    public string? KnockdownAvg { get; set; }

    [JsonPropertyName("averageFightTimeSeconds")]
    public int? AverageFightTimeSeconds { get; set; }

    [JsonPropertyName("sigStrikesByPosition")]
    public StrikeBreakdown? SigStrikesByPosition { get; set; }

    [JsonPropertyName("sigStrikesByTarget")]
    public StrikeBreakdown? SigStrikesByTarget { get; set; }
}

class StrikeBreakdown
{
    [JsonPropertyName("standing")]
    public StrikeSegment? Standing { get; set; }

    [JsonPropertyName("clinch")]
    public StrikeSegment? Clinch { get; set; }

    [JsonPropertyName("ground")]
    public StrikeSegment? Ground { get; set; }

    [JsonPropertyName("head")]
    public StrikeSegment? Head { get; set; }

    [JsonPropertyName("body")]
    public StrikeSegment? Body { get; set; }

    [JsonPropertyName("leg")]
    public StrikeSegment? Leg { get; set; }
}

class StrikeSegment
{
    [JsonPropertyName("count")]
    public int? Count { get; set; }

    [JsonPropertyName("percent")]
    public decimal? Percent { get; set; }
}

class RawData
{
    [JsonPropertyName("heroStats")]
    public Dictionary<string, string>? HeroStats { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraData { get; set; }
}

class AppDbContext : DbContext
{
    public DbSet<Fighter> Fighters => Set<Fighter>();
    public DbSet<FighterHeroStat> FighterHeroStats => Set<FighterHeroStat>();
    public DbSet<FighterStats> FighterStats => Set<FighterStats>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<Bout> Bouts => Set<Bout>();
    public DbSet<BoutFighterStats> BoutFighterStats => Set<BoutFighterStats>();

    private readonly string _connectionString;

    public AppDbContext(string connectionString)
    {
        _connectionString = connectionString;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseNpgsql(_connectionString);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Fighter>().Property(f => f.RawJson).HasColumnType("jsonb");
        modelBuilder.Entity<Event>().Property(e => e.RawJson).HasColumnType("jsonb");

        modelBuilder.Entity<FighterStats>().HasKey(s => s.FighterId);
        modelBuilder.Entity<Fighter>()
            .HasOne(f => f.Stats)
            .WithOne()
            .HasForeignKey<FighterStats>(s => s.FighterId);

        // Three separate FK relationships to Fighters from the same Bout row (no
        // navigation collection back on Fighter, so each needs explicit configuration).
        // SetNull rather than Cascade: if a Fighter row is ever removed (e.g. the
        // duplicate-stub cleanup), the bout record and its result should survive.
        modelBuilder.Entity<Bout>()
            .HasOne<Fighter>().WithMany().HasForeignKey(b => b.Fighter1Id).OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<Bout>()
            .HasOne<Fighter>().WithMany().HasForeignKey(b => b.Fighter2Id).OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<Bout>()
            .HasOne<Fighter>().WithMany().HasForeignKey(b => b.WinnerFighterId).OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<Bout>()
            .HasOne<Event>().WithMany().HasForeignKey(b => b.EventId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<BoutFighterStats>()
            .HasOne<Bout>().WithMany().HasForeignKey(s => s.BoutId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<BoutFighterStats>()
            .HasOne<Fighter>().WithMany().HasForeignKey(s => s.FighterId).OnDelete(DeleteBehavior.SetNull);

        var utcConverter = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))
                {
                    property.SetValueConverter(utcConverter);
                }
            }
        }
    }
}

class AppDbContextFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddUserSecrets<Program>()
            .Build();

        string connectionString = config["ConnectionStrings:MmaDb"]!;
        return new AppDbContext(connectionString);
    }
}

