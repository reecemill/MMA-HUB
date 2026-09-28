using System.Globalization;

namespace Web;

// Display helpers for the scraper's text fields, where a missing value is often "",
// "0", or a status in another language rather than null.
public static class Format
{
    public const string Missing = "–";

    public static string Height(string? inches) =>
        double.TryParse(inches, CultureInfo.InvariantCulture, out double h) && h > 0
            ? $"{(int)h / 12}'{h % 12:0.#}\""
            : Missing;

    public static string Inches(string? inches) =>
        double.TryParse(inches, CultureInfo.InvariantCulture, out double v) && v > 0 ? $"{v:0.#}\"" : Missing;

    public static string Pounds(string? lbs) =>
        double.TryParse(lbs, CultureInfo.InvariantCulture, out double v) && v > 0 ? $"{v:0.#} lbs" : Missing;

    public static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? Missing : value;

    // About two thirds of nicknames come with their own quotes; show them all one way.
    public static string? Nickname(string? nickname)
    {
        string? bare = nickname?.Trim().Trim('"', '“', '”').Trim();
        return string.IsNullOrEmpty(bare) ? null : $"“{bare}”";
    }

    // Some fighters' statuses came from the Italian version of UFC.com.
    public static string Status(string? status) => status switch
    {
        "Attivo" => "Active",
        "Non combattere" => "Not Fighting",
        "Pensionato" => "Retired",
        null or "" => Missing,
        _ => status,
    };
}
