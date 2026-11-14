using Microsoft.EntityFrameworkCore;
using System.Text.Json;

// Scrapes UFC.com into the Ufc* tables (UfcComEntities.cs). Run from src/Scraper:
//   dotnet run                          download what's new, then rebuild the tables
//   dotnet run -- --all-athletes        also fetch every fighter's ufc.com page that isn't
//                                       saved yet (about 2,800 pages at 15 seconds each
//                                       the first time: run it overnight)
//   dotnet run -- --offline             rebuild the tables from the saved files only
//   dotnet run -- --download-only       download, but leave the database alone
//
// The first run downloads every event and fight, about 10,000 requests to the stats API
// (2-3 hours, one request at a time). Later runs only fetch upcoming events, new
// results, and the athlete pages of fighters who've just fought.
static class UfcComScraper
{
    // This many empty event ids past the newest event means we've reached the end. The
    // longest gap between real events is one id.
    const int EmptyIdsBeforeStopping = 20;

    // A fight's stats are re-downloaded until UFC.com marks them official, but only for
    // this long after the event: some old fights never get marked.
    static readonly TimeSpan OfficialStatsWindow = TimeSpan.FromDays(30);

    // Without --all-athletes, only fighters on cards this close to today get their
    // athlete page fetched.
    static readonly TimeSpan AthleteWindow = TimeSpan.FromDays(14);

    // UFC.com takes a day or two after an event to update fighters' career numbers, so
    // an athlete page saved within this long of their latest fight is fetched again.
    static readonly TimeSpan AthletePageSettleTime = TimeSpan.FromDays(3);

    public static async Task Run(string? connectionString, string[] args)
    {
        var client = new UfcComClient(FindCacheDir());
        Console.WriteLine($"UFC.com files are saved in {client.CacheDir}");

        if (!args.Contains("--offline"))
        {
            List<LiveEvent> events = await DownloadEvents(client);
            await DownloadFights(client, events);
            await client.DownloadPage("/rankings", RankingsFile);
            await DownloadAthletes(client, events, args.Contains("--all-athletes"));
        }

        if (args.Contains("--download-only")) return;

        UfcComTables tables = BuildTables(client);
        Save(connectionString ?? throw new InvalidOperationException("ConnectionStrings:MmaDb isn't set."), tables);
    }

    static string EventFile(int id) => Path.Combine("events", $"{id}.json");
    static string FightFile(int id) => Path.Combine("fights", $"{id}.json");
    static string AthleteFile(string slug) => Path.Combine("athletes", $"{slug}.html");
    const string RankingsFile = "rankings.html";

    // data/ufc-com at the top of the repo (found by walking up to the .git folder), which
    // .gitignore already leaves out.
    static string FindCacheDir()
    {
        for (DirectoryInfo? dir = new(Directory.GetCurrentDirectory()); dir != null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
                return Path.Combine(dir.FullName, "data", "ufc-com");
        }
        return Path.Combine(Directory.GetCurrentDirectory(), "data", "ufc-com");
    }

    // Event ids count up from 1 (UFC 92 is 1, UFC 19 is 100, UFC 331 is 1335) and include
    // other promotions, and there's no list of them, so walk the ids until a run of empty
    // ones past the newest event. Finished and canceled events don't change, so their
    // saved copies are reused; upcoming ones are fetched again every run.
    static async Task<List<LiveEvent>> DownloadEvents(UfcComClient client)
    {
        Dictionary<int, LiveEvent> saved = LoadSavedEvents(client);
        int newestSaved = saved.Values.Where(e => !e.IsEmpty).Select(e => e.EventId).DefaultIfEmpty(0).Max();

        var events = new List<LiveEvent>();
        int newestFound = 0, downloaded = 0;
        for (int id = 1; id <= Math.Max(newestSaved, newestFound) + EmptyIdsBeforeStopping; id++)
        {
            saved.TryGetValue(id, out LiveEvent? ev);

            // An empty id before the newest saved event stays empty; past it, a newly
            // announced event may have taken it since the last run.
            bool reuse = ev != null && (ev.IsEmpty ? id < newestSaved : ev.IsFinished || ev.IsCanceled);
            if (!reuse)
            {
                string? json = await client.DownloadApi($"event/live/{id}.json", EventFile(id));
                if (json != null) ev = TryParse(json, UfcComJson.ParseEvent, EventFile(id)) ?? new LiveEvent();
                if (++downloaded % 100 == 0) Console.WriteLine($"Downloaded {downloaded} events (up to id {id})...");
            }

            if (ev == null || ev.IsEmpty) continue;
            newestFound = id;
            events.Add(ev);
        }

        Console.WriteLine($"Events: {events.Count(e => e.IsUfc)} UFC ({events.Count} in all), newest id {newestFound}, {downloaded} downloaded.");
        return events.Where(e => e.IsUfc).ToList();
    }

    static async Task DownloadFights(UfcComClient client, List<LiveEvent> events)
    {
        DateTime recent = DateTime.UtcNow - OfficialStatsWindow;
        var finished = events
            .SelectMany(e => (e.FightCard ?? []).Where(f => f.IsFinished).Select(f => (Fight: f, Recent: e.StartTime > recent)))
            .ToList();

        int downloaded = 0;
        foreach (var (fight, isRecent) in finished)
        {
            string? savedJson = client.ReadCached(FightFile(fight.FightId));
            if (savedJson != null && !isRecent) continue;
            if (savedJson != null && TryParse(savedJson, UfcComJson.ParseFight, FightFile(fight.FightId))?.OfficialStats == true) continue;

            await client.DownloadApi($"fight/live/{fight.FightId}.json", FightFile(fight.FightId));
            if (++downloaded % 250 == 0) Console.WriteLine($"Downloaded {downloaded} fights...");
        }
        Console.WriteLine($"Fights: {finished.Count} finished, {downloaded} downloaded.");
    }

    // Career numbers on an athlete page only change after a fight, so a saved page is
    // kept unless it was fetched before the fighter's latest fight had settled.
    static async Task DownloadAthletes(UfcComClient client, List<LiveEvent> events, bool all)
    {
        DateTime now = DateTime.UtcNow;
        var latestFight = new Dictionary<string, DateTime>();
        var wanted = new HashSet<string>();
        var nameSlugs = new Dictionary<string, string>();

        foreach (LiveEvent ev in events)
        {
            DateTime when = ev.StartTime ?? DateTime.MinValue;
            bool nearToday = when > now - AthleteWindow && when < now + AthleteWindow;
            foreach (LiveFight fight in ev.FightCard ?? [])
            {
                foreach (LiveFighter fighter in fight.Fighters ?? [])
                {
                    if (UfcComText.SlugFromLink(fighter.UFCLink) is not string slug) continue;
                    if (fight.IsFinished && (!latestFight.TryGetValue(slug, out DateTime last) || when > last)) latestFight[slug] = when;
                    if (all || nearToday) wanted.Add(slug);
                    if (UfcComText.Slugify($"{fighter.Name?.FirstName} {fighter.Name?.LastName}") is string nameSlug)
                        nameSlugs[slug] = nameSlug;
                }
            }
        }

        List<string> toFetch = wanted.Where(slug =>
        {
            string path = client.CachePath(AthleteFile(slug));
            if (!File.Exists(path)) return true;
            return latestFight.TryGetValue(slug, out DateTime last) && File.GetLastWriteTimeUtc(path) < last + AthletePageSettleTime;
        }).Order().ToList();

        Console.WriteLine($"Athlete pages: {toFetch.Count} to fetch (about {toFetch.Count / 4} minutes at 15 seconds each).");
        int fetched = 0;
        foreach (string slug in toFetch)
        {
            string? html = await client.DownloadPage($"/athlete/{slug}", AthleteFile(slug));

            // The API's link sometimes has the name the other way round from the page's
            // address (Xiaonan-Yan for ufc.com/athlete/yan-xiaonan), so on a 404 try the
            // name as the API writes it. Saved under the link's slug either way.
            if (html == "" && nameSlugs.TryGetValue(slug, out string? nameSlug) && nameSlug != slug)
                await client.DownloadPage($"/athlete/{nameSlug}", AthleteFile(slug));
            if (++fetched % 20 == 0) Console.WriteLine($"Fetched {fetched} of {toFetch.Count} athlete pages...");
        }
    }

    static Dictionary<int, LiveEvent> LoadSavedEvents(UfcComClient client)
    {
        var saved = new Dictionary<int, LiveEvent>();
        string dir = client.CachePath("events");
        if (!Directory.Exists(dir)) return saved;

        foreach (string path in Directory.EnumerateFiles(dir, "*.json"))
        {
            if (!int.TryParse(Path.GetFileNameWithoutExtension(path), out int id)) continue;
            saved[id] = TryParse(File.ReadAllText(path), UfcComJson.ParseEvent, path) ?? new LiveEvent();
        }
        return saved;
    }

    // A file that doesn't parse is reported and skipped rather than stopping the run.
    static T? TryParse<T>(string json, Func<string, T?> parse, string name) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return parse(json);
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Couldn't read {name}: {ex.Message}");
            return null;
        }
    }

    // Everything below works from the saved files only.

    class UfcComTables
    {
        public List<UfcEvent> Events { get; } = [];
        public Dictionary<int, UfcFighter> Fighters { get; } = [];
        public List<UfcFight> Fights { get; } = [];
        public List<UfcFightFighter> FightFighters { get; } = [];
        public List<UfcFightStats> FightStats { get; } = [];
        public List<UfcRoundStats> RoundStats { get; } = [];
        public List<UfcFightScore> FightScores { get; } = [];
        public List<UfcRoundScore> RoundScores { get; } = [];
        public List<UfcFightAction> Actions { get; } = [];
        public HashSet<int> ActionIds { get; } = [];
        public Dictionary<int, string> LatestWeightClass { get; } = [];
        public List<UfcFighterProfile> Profiles { get; } = [];
        public List<UfcRanking> Rankings { get; } = [];
    }

    static UfcComTables BuildTables(UfcComClient client)
    {
        var tables = new UfcComTables();

        // Oldest first, so each fighter ends up with the details from their latest fight.
        List<LiveEvent> events = LoadSavedEvents(client).Values
            .Where(e => !e.IsEmpty && e.IsUfc)
            .OrderBy(e => e.StartTime).ThenBy(e => e.EventId)
            .ToList();

        var fightIds = new HashSet<int>();
        int repeatedFights = 0;
        foreach (LiveEvent ev in events)
        {
            tables.Events.Add(new UfcEvent
            {
                Id = ev.EventId,
                Name = ev.Name ?? "",
                StartTime = ev.StartTime,
                Date = UfcComText.LocalDate(ev.StartTime, ev.TimeZone),
                TimeZone = ev.TimeZone,
                Status = ev.Status,
                VenueId = ev.Location?.VenueId,
                Venue = ev.Location?.Venue,
                City = ev.Location?.City,
                State = ev.Location?.State,
                Country = ev.Location?.Country,
                CountryCode = ev.Location?.TriCode,
            });

            foreach (LiveFight fight in ev.FightCard ?? [])
            {
                if (!fightIds.Add(fight.FightId))
                {
                    repeatedFights++;
                    continue;
                }
                LiveFight? detail = fight.IsFinished
                    ? TryParse(client.ReadCached(FightFile(fight.FightId)) ?? "", UfcComJson.ParseFight, FightFile(fight.FightId))
                    : null;
                AddFight(tables, ev, fight, detail);
            }
        }
        if (repeatedFights > 0) Console.WriteLine($"Skipped {repeatedFights} fights listed on more than one event.");

        FinishFighters(tables);
        Dictionary<string, int> fighterIdBySlug = tables.Fighters.Values.ToDictionary(f => f.Slug!, f => f.Id);

        foreach (UfcFighter fighter in tables.Fighters.Values)
        {
            string path = client.CachePath(AthleteFile(fighter.Slug));
            if (!File.Exists(path)) continue;
            string html = File.ReadAllText(path);
            if (html.Length == 0) continue;

            UfcFighterProfile profile = UfcComPages.ParseAthlete(html);
            profile.FighterId = fighter.Id;
            profile.Slug = fighter.Slug;
            profile.FetchedAt = File.GetLastWriteTimeUtc(path);
            tables.Profiles.Add(profile);
        }

        // Rankings link to the page's address, which for a few fighters doesn't match the
        // API's link (see DownloadAthletes); fall back to the name.
        Dictionary<string, int> fighterIdByName = tables.Fighters.Values
            .Select(f => (Id: f.Id, Name: UfcComText.Slugify($"{f.FirstName} {f.LastName}")))
            .Where(f => f.Name != null)
            .GroupBy(f => f.Name!)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single().Id);

        string rankingsPath = client.CachePath(RankingsFile);
        if (File.Exists(rankingsPath))
        {
            DateTime fetchedAt = File.GetLastWriteTimeUtc(rankingsPath);
            foreach (UfcRanking ranking in UfcComPages.ParseRankings(File.ReadAllText(rankingsPath)))
            {
                ranking.FighterId =
                    fighterIdBySlug.TryGetValue(ranking.FighterSlug, out int id) ? id
                    : UfcComText.Slugify(ranking.FighterName) is string name && fighterIdByName.TryGetValue(name, out id) ? id
                    : null;
                ranking.FetchedAt = fetchedAt;
                tables.Rankings.Add(ranking);
            }
        }

        return tables;
    }

    // Every fighter gets a unique slug, since the site uses it in their page's address.
    // A fighter the API has no link for gets one from their name. Two fighters can share
    // a slug (there are two Bruno Silvas); neither can be told apart from the other's
    // athlete page, so both get their id added and go without a profile. Fighters the API
    // lists without a weight class get the one from their latest fight.
    static void FinishFighters(UfcComTables tables)
    {
        foreach (UfcFighter fighter in tables.Fighters.Values)
        {
            fighter.Slug ??= UfcComText.Slugify($"{fighter.FirstName} {fighter.LastName}") ?? fighter.Id.ToString();
            fighter.WeightClass ??= tables.LatestWeightClass.GetValueOrDefault(fighter.Id);
        }
        foreach (var shared in tables.Fighters.Values.GroupBy(f => f.Slug).Where(g => g.Count() > 1).ToList())
        {
            foreach (UfcFighter fighter in shared) fighter.Slug = $"{fighter.Slug}-{fighter.Id}";
        }
    }

    // The event JSON gives the card and the fighters; the fight JSON, once a fight is
    // over, repeats the result and adds the stats and judges' round scores.
    static void AddFight(UfcComTables tables, LiveEvent ev, LiveFight fight, LiveFight? detail)
    {
        LiveResult? result = detail?.Result ?? fight.Result;
        List<LiveFighter> fighters = (fight.Fighters ?? []).DistinctBy(f => f.FighterId).ToList();
        foreach (LiveFighter fighter in fighters)
        {
            MergeFighter(tables.Fighters, fighter);
            if (UfcComText.Clean(fight.WeightClass?.Description) is string weightClass)
                tables.LatestWeightClass[fighter.FighterId] = weightClass;
        }
        HashSet<int> inFight = fighters.Select(f => f.FighterId).ToHashSet();

        LiveAccolade? belt = fight.Accolades?.FirstOrDefault(a => a.Type == "Belt");
        tables.Fights.Add(new UfcFight
        {
            Id = fight.FightId,
            EventId = ev.EventId,
            FightOrder = fight.FightOrder,
            Status = fight.Status,
            CardSegment = fight.CardSegment,
            CardSegmentStartTime = fight.CardSegmentStartTime,
            CardSegmentBroadcaster = fight.CardSegmentBroadcaster,
            WeightClassId = fight.WeightClass?.WeightClassId,
            WeightClass = fight.WeightClass?.Description,
            IsTitleFight = belt != null,
            TitleName = belt?.Name,
            ScheduledRounds = fight.RuleSet?.PossibleRounds,
            RuleSet = fight.RuleSet?.Description,
            RefereeId = fight.Referee?.RefereeId,
            Referee = UfcComText.Clean($"{fight.Referee?.FirstName} {fight.Referee?.LastName}"),
            WinnerFighterId = fighters.FirstOrDefault(f => f.Outcome?.Outcome == "Win")?.FighterId,
            Method = result?.Method,
            EndingRound = result?.EndingRound,
            EndingTime = result?.EndingTime,
            DurationSeconds = UfcComText.DurationSeconds(fight.RuleSet?.Description, result?.EndingRound, result?.EndingTime),
            EndingStrike = result?.EndingStrike,
            EndingTarget = result?.EndingTarget,
            EndingPosition = result?.EndingPosition,
            EndingSubmission = result?.EndingSubmission,
            EndingNotes = result?.EndingNotes,
            FightOfTheNight = result?.FightOfTheNight ?? false,
            OfficialStats = detail?.OfficialStats,
        });

        tables.FightFighters.AddRange(fighters.Select(f => new UfcFightFighter
        {
            FightId = fight.FightId,
            FighterId = f.FighterId,
            Corner = f.Corner,
            Outcome = f.Outcome?.Outcome,
            WeighIn = f.WeighIn,
            KOOfTheNight = f.KOOfTheNight,
            SubmissionOfTheNight = f.SubmissionOfTheNight,
            PerformanceOfTheNight = f.PerformanceOfTheNight,
        }));

        foreach (UfcFightStats stats in (detail?.FightStats ?? []).Where(s => inFight.Contains(s.FighterId)).DistinctBy(s => s.FighterId))
        {
            stats.FightId = fight.FightId;
            tables.FightStats.Add(stats);
        }

        foreach (LiveFighterRounds fighterRounds in (detail?.RoundStats ?? []).Where(r => inFight.Contains(r.FighterId)).DistinctBy(r => r.FighterId))
        {
            foreach (UfcRoundStats round in (fighterRounds.Rounds ?? []).DistinctBy(r => r.Round))
            {
                round.FightId = fight.FightId;
                round.FighterId = fighterRounds.FighterId;
                tables.RoundStats.Add(round);
            }
        }

        foreach (LiveJudgeTotal judge in (result?.FightScores ?? []).DistinctBy(j => j.JudgeId))
        {
            tables.FightScores.AddRange((judge.Fighters ?? []).Where(f => inFight.Contains(f.FighterId)).DistinctBy(f => f.FighterId)
                .Select(f => new UfcFightScore
                {
                    FightId = fight.FightId,
                    JudgeId = judge.JudgeId,
                    FighterId = f.FighterId,
                    JudgeName = UfcComText.Clean($"{judge.JudgeFirstName} {judge.JudgeLastName}"),
                    Score = f.Score,
                }));
        }

        foreach (LiveJudgeRounds judge in (result?.RoundScores ?? []).DistinctBy(j => j.JudgeId))
        {
            foreach (LiveRoundScore round in (judge.Rounds ?? []).DistinctBy(r => r.RoundNumber))
            {
                tables.RoundScores.AddRange((round.Fighters ?? []).Where(f => inFight.Contains(f.FighterId)).DistinctBy(f => f.FighterId)
                    .Select(f => new UfcRoundScore
                    {
                        FightId = fight.FightId,
                        JudgeId = judge.JudgeId,
                        Round = round.RoundNumber,
                        FighterId = f.FighterId,
                        JudgeName = UfcComText.Clean($"{judge.JudgeFirstName} {judge.JudgeLastName}"),
                        Score = f.Score,
                    }));
            }
        }

        tables.Actions.AddRange((fight.FightNightTracking ?? []).Where(a => tables.ActionIds.Add(a.ActionId)).Select(a => new UfcFightAction
        {
            Id = a.ActionId,
            FightId = fight.FightId,
            FighterId = a.FighterId is int id && inFight.Contains(id) ? id : null,
            Type = a.Type,
            Round = a.RoundNumber,
            RoundTime = a.RoundTime,
            Timestamp = a.Timestamp,
        }));
    }

    // Later fights overwrite earlier ones, except where the later one leaves a field blank.
    static void MergeFighter(Dictionary<int, UfcFighter> fighters, LiveFighter f)
    {
        if (!fighters.TryGetValue(f.FighterId, out UfcFighter? fighter))
        {
            fighter = new UfcFighter { Id = f.FighterId };
            fighters[f.FighterId] = fighter;
        }

        fighter.MmaId = f.MMAId ?? fighter.MmaId;
        fighter.FirstName = UfcComText.Clean(f.Name?.FirstName) ?? fighter.FirstName;
        fighter.LastName = UfcComText.Clean(f.Name?.LastName) ?? fighter.LastName;
        fighter.NickName = UfcComText.Clean(f.Name?.NickName) ?? fighter.NickName;
        fighter.Slug = UfcComText.SlugFromLink(f.UFCLink) ?? fighter.Slug;
        fighter.DateOfBirth = f.DOB ?? fighter.DateOfBirth;
        fighter.BornCity = UfcComText.Clean(f.Born?.City) ?? fighter.BornCity;
        fighter.BornState = UfcComText.Clean(f.Born?.State) ?? fighter.BornState;
        fighter.BornCountry = UfcComText.Clean(f.Born?.Country) ?? fighter.BornCountry;
        fighter.FightingOutOfCity = UfcComText.Clean(f.FightingOutOf?.City) ?? fighter.FightingOutOfCity;
        fighter.FightingOutOfState = UfcComText.Clean(f.FightingOutOf?.State) ?? fighter.FightingOutOfState;
        fighter.FightingOutOfCountry = UfcComText.Clean(f.FightingOutOf?.Country) ?? fighter.FightingOutOfCountry;
        fighter.Stance = UfcComText.Clean(f.Stance) ?? fighter.Stance;
        fighter.HeightInches = f.Height > 0 ? f.Height : fighter.HeightInches;
        fighter.ReachInches = f.Reach > 0 ? f.Reach : fighter.ReachInches;
        fighter.WeightLbs = f.Weight > 0 ? f.Weight : fighter.WeightLbs;
        fighter.WeightClass = UfcComText.Clean(f.WeightClasses?.OrderBy(w => w.WeightClassOrder ?? int.MaxValue).FirstOrDefault()?.Description)
            ?? fighter.WeightClass;
        if (f.Record != null)
        {
            fighter.Wins = f.Record.Wins;
            fighter.Losses = f.Record.Losses;
            fighter.Draws = f.Record.Draws;
            fighter.NoContests = f.Record.NoContests;
        }
    }

    // The tables are rebuilt from scratch in one transaction: simpler than matching rows
    // up, and a fight UFC.com drops from a card disappears here too.
    static void Save(string connectionString, UfcComTables tables)
    {
        using AppDbContext db = new AppDbContext(connectionString);
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        using var transaction = db.Database.BeginTransaction();

        db.UfcRankings.ExecuteDelete();
        db.UfcFighterProfiles.ExecuteDelete();
        db.UfcFightActions.ExecuteDelete();
        db.UfcRoundScores.ExecuteDelete();
        db.UfcFightScores.ExecuteDelete();
        db.UfcRoundStats.ExecuteDelete();
        db.UfcFightStats.ExecuteDelete();
        db.UfcFightFighters.ExecuteDelete();
        db.UfcFights.ExecuteDelete();
        db.UfcEvents.ExecuteDelete();
        db.UfcFighters.ExecuteDelete();

        Insert(db, tables.Events);
        Insert(db, tables.Fighters.Values.ToList());
        Insert(db, tables.Fights);
        Insert(db, tables.FightFighters);
        Insert(db, tables.FightStats);
        Insert(db, tables.RoundStats);
        Insert(db, tables.FightScores);
        Insert(db, tables.RoundScores);
        Insert(db, tables.Actions);
        Insert(db, tables.Profiles);
        Insert(db, tables.Rankings);

        transaction.Commit();
    }

    static void Insert<T>(AppDbContext db, List<T> rows) where T : class
    {
        foreach (T[] chunk in rows.Chunk(5000))
        {
            db.Set<T>().AddRange(chunk);
            db.SaveChanges();
            db.ChangeTracker.Clear();
        }
        Console.WriteLine($"Saved {rows.Count} {typeof(T).Name} rows.");
    }
}
