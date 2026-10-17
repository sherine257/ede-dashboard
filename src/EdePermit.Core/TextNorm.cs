using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EdePermit.Core;

public static class TextNorm
{
    static readonly Regex NonWord = new("[^a-z0-9]+", RegexOptions.Compiled);
    static readonly (string From, string To)[] Mojibake =
    [
        ("\u00E2\u201E\u00A2", "™"), // UTF-8 ™ read as Windows-1252
        ("\u00C2\u00AE", "®"),       // ®
        ("\u00C2\u00A9", "©"),       // ©
        ("\u00C2\u00A0", " "),       // nbsp
        ("\u00C2\u00B0", "°"),       // degree
        ("\u00E2\u20AC\u2122", "'"), // ’
        ("\u00E2\u20AC\u201C", "\""),
        ("\u00E2\u20AC\u009D", "\""),
        ("\u00E2\u20AC\u02DC", "'"),
        ("\u00E2\u20AC\u0090", "-"),
        ("\u00EF\u00B8\u008F", ""),
        ("™", " "),
        ("®", " "),
        ("©", " "),
        ("℠", " ")
    ];

    public static string Repair(string value)
    {
        foreach (var (from, to) in Mojibake)
            value = value.Replace(from, to);
        return value;
    }

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var folded = Repair(value).Trim().ToLowerInvariant().Normalize(NormalizationForm.FormKD);
        var sb = new StringBuilder(folded.Length);
        foreach (var ch in folded)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark) continue;
            sb.Append(ch);
        }
        return NonWord.Replace(sb.ToString(), " ").Trim();
    }

    public static string Compact(string? value) => Normalize(value).Replace(" ", "");

    public static bool ContainsNorm(string haystack, string needle)
    {
        var h = Normalize(haystack);
        var n = Normalize(needle);
        return n.Length >= 3 && h.Contains(n, StringComparison.Ordinal);
    }
}
