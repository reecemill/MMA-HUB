using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

IConfiguration config = new ConfigurationBuilder()
    .AddUserSecrets<Program>()
    .Build();

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
        }

        allEvents.AddRange(page.Data);
    }
}

Console.WriteLine(allEvents.Count);

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

class EventPageInfo
{
    [JsonPropertyName("meta")]
    public MetaInfo? Meta { get; set; }

    [JsonPropertyName("data")]
    public List<Event>? Data { get; set; }
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

    [JsonExtensionData]
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public Dictionary<string, JsonElement>? ExtraData { get; set; }

    public string? RawJson { get; set; }

    public List<FighterHeroStat> HeroStatRows { get; set; } = new List<FighterHeroStat>();
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

    [JsonExtensionData]
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public Dictionary<string, JsonElement>? ExtraData { get; set; }

    public string? RawJson { get; set; }
}

class FighterHeroStat
{
    public int Id { get; set; }
    public Guid FighterId { get; set; }
    public string StatKey { get; set; } = "";
    public string StatValue { get; set; } = "";
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
    public DbSet<Event> Events => Set<Event>();

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

