using System.Text.RegularExpressions;

// Cito lists most recent events twice under two slugs, e.g. "ufc-322" and
// "ufc-322-della-maddalena-vs-makhachev": same card, dates 0-1 days apart. It also
// sometimes puts an old bout on a newer event's card (UFC 329 carried fights from 2013
// and 2019). Left in, these show up twice on the site and count twice in fighter
// histories, and the model sees results before the fights it's predicting.
//
// Nothing is deleted: duplicates are marked (Event.DuplicateOfEventId,
// Bout.DuplicateOfBoutId) and the site skips marked rows. Every scrape re-saves events
// and bouts from the API, which clears the marks, so this runs at the end of each scrape.
// To run it on its own without calling the API: dotnet run -- --dedupe-only
static class EventDeduplicator
{
    // Two events are the same one if at least half of the smaller card (and at least
    // two bouts) are the same pairings. Real duplicates share 83-100%; events that
    // merely share a rematch or a stray old bout share 40% or less.
    const double MinSharedFraction = 0.5;
    const int MinSharedBouts = 2;

    public static void Run(string connectionString)
    {
        using AppDbContext db = new AppDbContext(connectionString);

        List<Event> events = db.Events.ToList();
        List<Bout> bouts = db.Bouts.ToList();
        events.ForEach(e => e.DuplicateOfEventId = null);
        bouts.ForEach(b => b.DuplicateOfBoutId = null);

        int markedEvents = MarkDuplicateEvents(events, bouts);
        int markedBouts = MarkRepeatedBouts(events, bouts);

        db.SaveChanges();
        Console.WriteLine($"Marked {markedEvents} duplicate events and {markedBouts} repeated bouts.");
    }

    static string? PairKey(Bout b) =>
        b.Fighter1Id is Guid a && b.Fighter2Id is Guid c
            ? (a.CompareTo(c) < 0 ? $"{a}|{c}" : $"{c}|{a}")
            : null;

    static int MarkDuplicateEvents(List<Event> events, List<Bout> bouts)
    {
        Dictionary<Guid, HashSet<string>> cards = bouts
            .Where(b => PairKey(b) != null)
            .GroupBy(b => b.EventId)
            .ToDictionary(g => g.Key, g => g.Select(b => PairKey(b)!).ToHashSet());

        // Only events that share at least one pairing need comparing.
        HashSet<(Guid, Guid)> candidatePairs = [];
        var eventsByPairing = cards
            .SelectMany(c => c.Value.Select(key => (Key: key, EventId: c.Key)))
            .GroupBy(x => x.Key, x => x.EventId);
        foreach (var sameFight in eventsByPairing)
        {
            List<Guid> ids = sameFight.Distinct().OrderBy(id => id).ToList();
            for (int i = 0; i < ids.Count; i++)
                for (int j = i + 1; j < ids.Count; j++)
                    candidatePairs.Add((ids[i], ids[j]));
        }

        // Union-find, so three copies of one event end up in one group.
        Dictionary<Guid, Guid> parent = [];
        Guid Find(Guid id) => parent.TryGetValue(id, out Guid p) && p != id ? parent[id] = Find(p) : id;

        foreach (var (a, b) in candidatePairs)
        {
            int shared = cards[a].Intersect(cards[b]).Count();
            int smaller = Math.Min(cards[a].Count, cards[b].Count);
            if (shared >= MinSharedBouts && shared >= MinSharedFraction * smaller)
                parent[Find(a)] = Find(b);
        }

        Dictionary<Guid, Event> byId = events.ToDictionary(e => e.Id);
        int marked = 0;
        foreach (var group in parent.Keys.Union(parent.Values).GroupBy(Find).Select(g => g.Select(id => byId[id]).ToList()))
        {
            // Keep the copy with the fullest card; on a tie, the one titled with its
            // headliners ("UFC 322: Della Maddalena vs. Makhachev" over "UFC 322").
            Event keep = group
                .OrderByDescending(e => cards.GetValueOrDefault(e.Id)?.Count ?? 0)
                .ThenByDescending(e => e.Title?.Contains(':') == true || Regex.IsMatch(e.Title ?? "", @" vs\.? "))
                .ThenBy(e => e.Id)
                .First();

            // When the copies are a day apart, the later date is the real (local) one;
            // the headliner-titled copy is the one that's a day early.
            List<DateTime> dates = group.Where(e => e.EventDate.HasValue).Select(e => e.EventDate!.Value).ToList();
            if (dates.Count > 1 && (dates.Max() - dates.Min()).TotalDays <= 2)
            {
                keep.EventDate = dates.Max();
            }
            else if (dates.Count > 1)
            {
                Console.WriteLine($"Warning: copies of {keep.Title} are dated " +
                    $"{string.Join(", ", dates.Select(d => d.ToString("yyyy-MM-dd")))}; kept {keep.EventDate:yyyy-MM-dd}.");
            }

            foreach (Event dup in group.Where(e => e.Id != keep.Id))
            {
                dup.DuplicateOfEventId = keep.Id;
                marked++;
            }
        }
        return marked;
    }

    // One fight listed twice: same pairing, winner, round, and finish time, either on the
    // same card (Cito sometimes lists a bout twice, with the method spelled two ways, e.g.
    // "U-DEC" and "Decision - Unanimous") or on another event. Across events that alone
    // could be a real rematch that went the distance twice (Whittaker beat Romero on points
    // at UFC 213 and UFC 225), so there it also has to be within two days or a stoppage,
    // which doesn't repeat to the second. Keeps the earliest copy.
    static int MarkRepeatedBouts(List<Event> events, List<Bout> bouts)
    {
        Dictionary<Guid, Event> kept = events.Where(e => e.DuplicateOfEventId == null).ToDictionary(e => e.Id);
        int marked = 0;
        var groups = bouts
            .Where(b => kept.ContainsKey(b.EventId) && b.Status == "completed" && PairKey(b) != null
                        && !string.IsNullOrEmpty(b.ResultTime))
            .GroupBy(b => (PairKey(b), b.WinnerFighterId, b.ResultRound, b.ResultTime))
            .Where(g => g.Count() > 1);

        foreach (var group in groups)
        {
            List<Bout> ordered = group
                .OrderBy(b => kept[b.EventId].EventDate ?? DateTime.MaxValue)
                .ThenBy(b => b.Id)
                .ToList();
            Bout first = ordered[0];
            DateTime? firstDate = kept[first.EventId].EventDate;
            bool wentTheDistance = first.ResultTime == "5:00";

            foreach (Bout repeat in ordered.Skip(1))
            {
                DateTime? date = kept[repeat.EventId].EventDate;
                bool sameCard = repeat.EventId == first.EventId;
                bool closeDates = firstDate.HasValue && date.HasValue && (date.Value - firstDate.Value).TotalDays <= 2;
                if (sameCard || closeDates || !wentTheDistance)
                {
                    repeat.DuplicateOfBoutId = first.Id;
                    marked++;
                }
            }
        }
        return marked;
    }
}
