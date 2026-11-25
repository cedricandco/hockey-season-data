using System.Globalization;
using System.Text;

namespace HockeySeason.Application;

/// <summary>
/// Matches a published team name against the base name an association
/// president types in the configuration page.
/// </summary>
/// <remarks>
/// Spordle names a team "GRIZZLY 1 STE-JULIE", "GRIZZLY 2 ST-HYACINTHE": the
/// base name is the part before the number and the city. The match is
/// case- and accent-insensitive, done on whole words — the name must start
/// with the base name followed by a word boundary — so "grizzly" selects every
/// GRIZZLY team of the association, "grizzly 1" only the first one, and
/// "grizzly" never selects "GRIZZLYS".
/// </remarks>
public static class TeamNameMatcher
{
    /// <summary>Whether the team name starts with the base name, on a word boundary.</summary>
    public static bool Matches(string? teamName, string? baseName)
    {
        var name = Normalize(teamName);
        var basis = Normalize(baseName);
        if (name.Length == 0 || basis.Length == 0)
        {
            return false;
        }

        return string.Equals(name, basis, StringComparison.Ordinal)
            || name.StartsWith(basis + " ", StringComparison.Ordinal);
    }

    /// <summary>
    /// Lower case, without diacritics, with a single space between words. Any
    /// punctuation or dash acts as a word separator, so "st-julie", "st julie"
    /// and "st—julie" are the same two words.
    /// </summary>
    public static string Normalize(string? value)
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
            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }
}
