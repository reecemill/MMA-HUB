using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using System.Globalization;
using System.Text.RegularExpressions;

// Reads the two ufc.com HTML pages the stats API doesn't cover: athlete pages (bio and
// career numbers) and the rankings. Values are looked up by their on-page labels
// ("Sig. Str. Defense", "Trains at") rather than by position, so a reordered page
// still parses; a label that disappears just leaves that column null.
static class UfcComPages
{
    static readonly HtmlParser Parser = new();

    public static UfcFighterProfile ParseAthlete(string html)
    {
        IHtmlDocument doc = Parser.ParseDocument(html);
        var profile = new UfcFighterProfile
        {
            Division = Text(doc.QuerySelector(".hero-profile__division-title")),
            RecordText = Text(doc.QuerySelector(".hero-profile__division-body")),
            ImageUrl = doc.QuerySelector("img.hero-profile__image")?.GetAttribute("src"),
        };

        // Tags above the name, e.g. "Flyweight Division", "#6 PFP", "Active", "Title Holder".
        foreach (string tag in doc.QuerySelectorAll(".hero-profile__tag").Select(Text).OfType<string>())
        {
            Match ranked = Regex.Match(tag, @"^#(\d+)\s+(.+)$");
            if (tag == "Title Holder") profile.IsChampion = true;
            else if (ranked.Success && ranked.Groups[2].Value == "PFP") profile.PoundForPoundRank = int.Parse(ranked.Groups[1].Value);
            else if (ranked.Success) profile.DivisionRank = int.Parse(ranked.Groups[1].Value);
        }

        Dictionary<string, string?> hero = Pairs(doc, ".hero-profile__stat", ".hero-profile__stat-text", ".hero-profile__stat-numb");
        profile.WinsByKnockout = UfcComText.FirstInt(Get(hero, "Wins by Knockout"));
        profile.WinsBySubmission = UfcComText.FirstInt(Get(hero, "Wins by Submission"));
        profile.FirstRoundFinishes = UfcComText.FirstInt(Get(hero, "First Round Finishes"));

        Dictionary<string, string?> bio = Pairs(doc, ".c-bio__field", ".c-bio__label", ".c-bio__text");
        profile.Status = Get(bio, "Status");
        profile.PlaceOfBirth = Get(bio, "Place of Birth");
        profile.TrainsAt = Get(bio, "Trains at");
        profile.FightingStyle = Get(bio, "Fighting style");
        profile.Age = UfcComText.FirstInt(Get(bio, "Age"));
        profile.HeightInches = UfcComText.FirstNumber(Get(bio, "Height"));
        profile.WeightLbs = UfcComText.FirstNumber(Get(bio, "Weight"));
        profile.ReachInches = UfcComText.FirstNumber(Get(bio, "Reach"));
        profile.LegReachInches = UfcComText.FirstNumber(Get(bio, "Leg reach"));
        profile.OctagonDebut = ParseDebut(Get(bio, "Octagon Debut"));

        // "Sig. Str. Landed / Per Min": 8.26, "Sig. Str. Defense": "57 %", "Average fight time": "14:14".
        Dictionary<string, string?> compare = Pairs(doc, ".c-stat-compare__group", ".c-stat-compare__label", ".c-stat-compare__number");
        profile.SigStrikesLandedPerMin = UfcComText.FirstNumber(Get(compare, "Sig. Str. Landed"));
        profile.SigStrikesAbsorbedPerMin = UfcComText.FirstNumber(Get(compare, "Sig. Str. Absorbed"));
        profile.TakedownAvgPer15Min = UfcComText.FirstNumber(Get(compare, "Takedown avg"));
        profile.SubmissionAvgPer15Min = UfcComText.FirstNumber(Get(compare, "Submission avg"));
        profile.SigStrikeDefense = UfcComText.FirstNumber(Get(compare, "Sig. Str. Defense"));
        profile.TakedownDefense = UfcComText.FirstNumber(Get(compare, "Takedown Defense"));
        profile.KnockdownAvg = UfcComText.FirstNumber(Get(compare, "Knockdown Avg"));
        profile.AverageFightSeconds = UfcComText.ClockSeconds(Get(compare, "Average fight time"));

        Dictionary<string, string?> totals = Pairs(doc, ".c-overlap__stats", ".c-overlap__stats-text", ".c-overlap__stats-value");
        profile.SigStrikesLanded = UfcComText.FirstInt(Get(totals, "Sig. Strikes Landed"));
        profile.SigStrikesAttempted = UfcComText.FirstInt(Get(totals, "Sig. Strikes Attempted"));
        profile.TakedownsLanded = UfcComText.FirstInt(Get(totals, "Takedowns Landed"));
        profile.TakedownsAttempted = UfcComText.FirstInt(Get(totals, "Takedowns Attempted"));

        // The accuracy circles: each sits in a block with its title ("Striking accuracy").
        foreach (IElement block in doc.QuerySelectorAll(".stats-records-inner"))
        {
            string? title = Text(block.QuerySelector(".c-overlap--stats__title"));
            decimal? percent = UfcComText.FirstNumber(Text(block.QuerySelector(".e-chart-circle__percent")));
            if (title == null || percent == null) continue;
            if (title.StartsWith("Striking", StringComparison.OrdinalIgnoreCase)) profile.StrikingAccuracy = percent;
            else if (title.StartsWith("Takedown", StringComparison.OrdinalIgnoreCase)) profile.TakedownAccuracy = percent;
        }

        // "Standing: 1230 (87%)" and "KO/TKO: 9 (50%)". The win-by-method counts here
        // include decisions, which the hero numbers above don't.
        Dictionary<string, string?> bars = Pairs(doc, ".c-stat-3bar__group", ".c-stat-3bar__label", ".c-stat-3bar__value");
        profile.SigStrikesStanding = UfcComText.FirstInt(Get(bars, "Standing"));
        profile.SigStrikesClinch = UfcComText.FirstInt(Get(bars, "Clinch"));
        profile.SigStrikesGround = UfcComText.FirstInt(Get(bars, "Ground"));
        profile.WinsByDecision = UfcComText.FirstInt(Get(bars, "DEC"));

        // Strikes by target are drawn as an SVG body with the numbers as <text> elements.
        profile.SigStrikesHead = UfcComText.FirstInt(Text(doc.GetElementById("e-stat-body_x5F__x5F_head_value")));
        profile.SigStrikesBody = UfcComText.FirstInt(Text(doc.GetElementById("e-stat-body_x5F__x5F_body_value")));
        profile.SigStrikesLeg = UfcComText.FirstInt(Text(doc.GetElementById("e-stat-body_x5F__x5F_leg_value")));

        return profile;
    }

    // One list per division, in page order: the champion (rank 0, in the caption) and
    // then ranks 1-15. The page repeats the divisions further down; only the first of
    // each is read.
    public static List<UfcRanking> ParseRankings(string html)
    {
        IHtmlDocument doc = Parser.ParseDocument(html);
        var rankings = new List<UfcRanking>();
        var seen = new HashSet<string>();

        foreach (IElement header in doc.QuerySelectorAll(".view-grouping-header"))
        {
            // "Men's Pound-for-Pound <span>Top Rank</span>" -> "Men's Pound-for-Pound"
            IElement copy = (IElement)header.Clone();
            copy.QuerySelector("span")?.Remove();
            string? division = Text(copy);
            if (division == null || !seen.Add(division)) continue;

            IElement? content = header.NextElementSibling;
            if (content == null) continue;
            bool poundForPound = division.Contains("Pound-for-Pound");

            IElement? champion = content.QuerySelector("caption h5 a");
            if (champion != null && !poundForPound)
                rankings.Add(Ranking(division, 0, champion, null));

            foreach (IElement row in content.QuerySelectorAll("tbody tr"))
            {
                int? rank = UfcComText.FirstInt(Text(row.QuerySelector(".views-field-weight-class-rank")));
                IElement? link = row.QuerySelector(".views-field-title a");
                if (rank == null || link == null) continue;
                rankings.Add(Ranking(division, rank.Value, link, RankChange(Text(row.QuerySelector(".views-field-weight-class-rank-change")))));
            }
        }
        return rankings;
    }

    // "Rank increased by 1" -> "+1", "Rank decreased by 2" -> "-2", "NR" (new to the
    // rankings) stays as it is.
    static string? RankChange(string? text)
    {
        if (text == null) return null;
        int? by = UfcComText.FirstInt(text);
        if (text.Contains("increased", StringComparison.OrdinalIgnoreCase)) return $"+{by}";
        if (text.Contains("decreased", StringComparison.OrdinalIgnoreCase)) return $"-{by}";
        return text;
    }

    static UfcRanking Ranking(string division, int rank, IElement link, string? change) => new()
    {
        Division = division,
        Rank = rank,
        RankChange = change,
        FighterSlug = UfcComText.SlugFromLink(link.GetAttribute("href")) ?? "",
        FighterName = Text(link),
    };

    // Label -> value for every element matching itemSelector, e.g. each bio field.
    static Dictionary<string, string?> Pairs(IParentNode doc, string itemSelector, string labelSelector, string valueSelector)
    {
        var pairs = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (IElement item in doc.QuerySelectorAll(itemSelector))
        {
            string? label = Text(item.QuerySelector(labelSelector));
            if (label != null) pairs.TryAdd(label, Text(item.QuerySelector(valueSelector)));
        }
        return pairs;
    }

    static string? Get(Dictionary<string, string?> pairs, string label) =>
        pairs.TryGetValue(label, out string? value) ? value : null;

    static string? Text(IElement? element) => UfcComText.Clean(element?.TextContent);

    // "Jun. 24, 2023"; some months are written out ("May 1, 2010"), and September may
    // be "Sept.", which .NET's "MMM" (Sep) doesn't read.
    static DateOnly? ParseDebut(string? text)
    {
        if (text == null) return null;
        string normalized = Regex.Replace(text.Replace(".", ""), @"\bSept\b", "Sep");
        string[] formats = ["MMM d, yyyy", "MMMM d, yyyy"];
        return DateOnly.TryParseExact(normalized, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date)
            ? date : null;
    }
}
