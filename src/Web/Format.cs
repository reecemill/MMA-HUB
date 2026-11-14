namespace Web;

// Display helpers for values that are often missing.
public static class Format
{
    public const string Missing = "–";

    public static string Height(decimal? inches) =>
        inches is decimal h && h > 0 ? $"{(int)h / 12}'{h % 12:0.#}\"" : Missing;

    public static string Inches(decimal? inches) => inches is decimal v && v > 0 ? $"{v:0.#}\"" : Missing;

    public static string Pounds(decimal? lbs) => lbs is decimal v && v > 0 ? $"{v:0.#} lbs" : Missing;

    public static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? Missing : value;

    public static string Date(DateOnly? date) => date?.ToString("MMM d, yyyy") ?? Missing;

    // 288 -> "4:48"
    public static string Clock(int seconds) => $"{seconds / 60}:{seconds % 60:00}";

    public static string Percent(double fraction) => (fraction * 100).ToString("0") + "%";

    public static string? Nickname(string? nickname)
    {
        string? bare = nickname?.Trim().Trim('"', '“', '”').Trim();
        return string.IsNullOrEmpty(bare) ? null : $"“{bare}”";
    }

    // The parts that aren't blank, comma-separated: ("Houston", null, "USA") -> "Houston, USA".
    // A part that repeats the one before it is dropped ("Abu Dhabi", "Abu Dhabi", "UAE").
    public static string? Join(params string?[] parts)
    {
        List<string> kept = [];
        foreach (string part in parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()))
        {
            if (kept.Count == 0 || !kept[^1].Equals(part, StringComparison.OrdinalIgnoreCase)) kept.Add(part);
        }
        return kept.Count > 0 ? string.Join(", ", kept) : null;
    }
}
