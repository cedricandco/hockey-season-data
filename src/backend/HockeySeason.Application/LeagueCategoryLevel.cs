using System.Globalization;
using System.Text;

namespace HockeySeason.Application;

/// <summary>
/// Category level of a league — "M11", "M13", "M15", "M17", "M19" or "Junior"
/// — read from its published French name. A category director follows a whole
/// level regardless of the letter that refines it: "M13 A", "M13 B" and
/// "M13 AA" are all the M13 category.
/// </summary>
public static class LeagueCategoryLevel
{
    /// <summary>Level of a Junior category, in upper case like the M levels.</summary>
    public const string Junior = "JUNIOR";

    private static readonly string[] PreferredOrder = ["M11", "M13", "M15", "M17", "M19", Junior];

    /// <summary>
    /// Level of a published league name, or null when the name carries none
    /// (e.g. "Atome BB"). The level is the first "M" followed by digits
    /// ("M11A" → M11) or the word Junior (also Juvénil); the letters that
    /// refine the category ("A", "B", "AA") are deliberately ignored.
    /// </summary>
    public static string? FromLeagueName(string? leagueName)
    {
        var normalized = Normalize(leagueName);
        if (normalized.Length == 0)
        {
            return null;
        }

        var first = normalized.Split(' ')[0];
        if (first.StartsWith("JUNIOR", StringComparison.Ordinal) || first.StartsWith("JUVENIL", StringComparison.Ordinal))
        {
            return Junior;
        }

        if (first.Length >= 2 && first[0] == 'M' && char.IsAsciiDigit(first[1]))
        {
            var lastDigit = 1;
            while (lastDigit + 1 < first.Length && char.IsAsciiDigit(first[lastDigit + 1]))
            {
                lastDigit++;
            }
            return first[..(lastDigit + 1)];
        }

        return null;
    }

    /// <summary>
    /// Whether the league belongs to the category level, case- and
    /// accent-insensitively ("m13" selects "M13 AA").
    /// </summary>
    public static bool Matches(string? leagueName, string? category) =>
        category is not null
        && FromLeagueName(leagueName) is { } level
        && string.Equals(level, FromCategory(category), StringComparison.Ordinal);

    /// <summary>Canonical level of a typed category: "m13 " → "M13", "Junior" → "JUNIOR".</summary>
    public static string FromCategory(string? category) => FromLeagueName(category) ?? Normalize(category);

    /// <summary>Display name of a level: "M13" stays, "JUNIOR" reads "Junior".</summary>
    public static string Display(string level) =>
        string.Equals(level, Junior, StringComparison.Ordinal) ? "Junior" : level;

    /// <summary>
    /// M11 → M13 → M15 → M17 → M19 → Junior first, then the other levels by
    /// name, so the director's picker always reads in the age order. Levels are
    /// canonicalized first, so "Junior" and "JUNIOR" rank the same.
    /// </summary>
    public static IComparer<string> Comparer { get; } =
        System.Collections.Generic.Comparer<string>.Create(static (left, right) =>
        {
            var leftLevel = FromCategory(left);
            var rightLevel = FromCategory(right);
            var leftIndex = Array.IndexOf(PreferredOrder, leftLevel);
            var rightIndex = Array.IndexOf(PreferredOrder, rightLevel);
            if (leftIndex < 0) leftIndex = int.MaxValue;
            if (rightIndex < 0) rightIndex = int.MaxValue;
            return leftIndex != rightIndex
                ? leftIndex.CompareTo(rightIndex)
                : string.CompareOrdinal(leftLevel, rightLevel);
        });

    /// <summary>
    /// Upper case, without diacritics, with a single space between words: any
    /// dash or punctuation acts as a word separator, so "M13-B" and "M13 B"
    /// are the same two words.
    /// </summary>
    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var pendingSeparator = false;
        foreach (var character in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (!char.IsLetterOrDigit(character))
            {
                pendingSeparator = builder.Length > 0;
                continue;
            }

            if (pendingSeparator)
            {
                builder.Append(' ');
                pendingSeparator = false;
            }
            builder.Append(char.ToUpperInvariant(character));
        }

        return builder.ToString();
    }
}
