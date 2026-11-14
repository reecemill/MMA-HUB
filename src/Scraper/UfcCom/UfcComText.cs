using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

// Small text helpers shared by the JSON and HTML parsing.
static class UfcComText
{
    // "4:48" -> 288.
    public static int? ClockSeconds(string? text)
    {
        Match match = Regex.Match(text ?? "", @"^\s*(\d+):(\d{1,2})\s*$");
        return match.Success ? int.Parse(match.Groups[1].Value) * 60 + int.Parse(match.Groups[2].Value) : null;
    }

    // The first number in the text: "57 %" -> 57, "125 lbs" -> 125, "1230 (87%)" -> 1230.
    public static decimal? FirstNumber(string? text)
    {
        Match match = Regex.Match(text ?? "", @"\d+(\.\d+)?");
        return match.Success ? decimal.Parse(match.Value, CultureInfo.InvariantCulture) : null;
    }

    public static int? FirstInt(string? text) => FirstNumber(text) is decimal value ? (int)value : null;

    // Collapses runs of whitespace; blank -> null.
    public static string? Clean(string? text)
    {
        string cleaned = Regex.Replace(text ?? "", @"\s+", " ").Trim();
        return cleaned.Length > 0 ? cleaned : null;
    }

    // "http://www.ufc.com/athlete/Joshua-Van" -> "joshua-van".
    public static string? SlugFromLink(string? link)
    {
        if (string.IsNullOrEmpty(link)) return null;
        int start = link.IndexOf("/athlete/", StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        return Slugify(Uri.UnescapeDataString(link[(start + "/athlete/".Length)..].Split('?', '#', '/')[0]));
    }

    // UFC.com's page addresses drop accents and punctuation, but the API's links keep
    // them: "Khalil-Rountree-Jr." is ufc.com/athlete/khalil-rountree-jr, "Jake-O'Brien"
    // is jake-obrien, "Thiago-Perpétuo" is thiago-perpetuo. Also turns a name into the
    // slug UFC.com would give it ("Yan Xiaonan" -> "yan-xiaonan").
    public static string? Slugify(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var slug = new StringBuilder();
        foreach (char c in text.ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9')) slug.Append(c);
            else if (c is '-' or ' ' or '_') slug.Append('-');
            else if (Transliterations.TryGetValue(c, out string? latin)) slug.Append(latin);
            // Anything else (accent marks split off by FormD, apostrophes, dots) is dropped.
        }
        string cleaned = Regex.Replace(slug.ToString(), "-{2,}", "-").Trim('-');
        return cleaned.Length > 0 ? cleaned : null;
    }

    // Letters that FormD doesn't split into a plain letter plus an accent.
    static readonly Dictionary<char, string> Transliterations = new()
    {
        ['ø'] = "o", ['ł'] = "l", ['đ'] = "d", ['æ'] = "ae", ['œ'] = "oe", ['ß'] = "ss", ['ı'] = "i",
    };

    // The event's own calendar date, from its start time and the venue's offset
    // ("GMT+08:00"): a Shanghai card starting 07:00 UTC on Aug 29 is on Aug 29 there, but
    // an evening card in Australia starting 23:00 UTC is on the next day.
    public static DateOnly? LocalDate(DateTime? utc, string? timeZone)
    {
        if (utc is not DateTime start) return null;
        Match offset = Regex.Match(timeZone ?? "", @"GMT([+-])(\d{1,2}):(\d{2})");
        if (offset.Success)
        {
            var span = new TimeSpan(int.Parse(offset.Groups[2].Value), int.Parse(offset.Groups[3].Value), 0);
            start += offset.Groups[1].Value == "-" ? -span : span;
        }
        return DateOnly.FromDateTime(start);
    }

    // How long a fight lasted, from the round lengths in its rule set ("3 Rnd (5-5-5)",
    // in minutes) and the round and time it ended. Early UFC rule sets without round
    // lengths only work out when the fight ended in the first round.
    public static int? DurationSeconds(string? ruleSet, int? endingRound, string? endingTime)
    {
        if (endingRound is not int round || round < 1 || ClockSeconds(endingTime) is not int intoRound) return null;
        if (round == 1) return intoRound;

        Match lengths = Regex.Match(ruleSet ?? "", @"\(([\d\s\-]+)\)");
        if (!lengths.Success) return null;
        List<int> minutes = lengths.Groups[1].Value.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(int.Parse).ToList();
        if (minutes.Count < round - 1) return null;
        return minutes.Take(round - 1).Sum() * 60 + intoRound;
    }
}
