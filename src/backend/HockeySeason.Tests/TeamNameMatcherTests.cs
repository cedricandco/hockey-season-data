using HockeySeason.Application;
using Xunit;

namespace HockeySeason.Tests;

/// <summary>
/// The base-name match of the association-president selector: a typed base
/// name must select every team of it (all leagues, divisions, categories) and
/// nothing else.
/// </summary>
public sealed class TeamNameMatcherTests
{
    [Theory]
    [InlineData("GRIZZLY 1 STE-JULIE", "grizzly")]
    [InlineData("GRIZZLY 2 ST-HYACINTHE", "Grizzly")]
    [InlineData("GRIZZLY 1 STE-JULIE", "grizzly 1")]
    [InlineData("GRIZZLY 1 STE-JULIE", " GRIZZLY ")]
    [InlineData("GRIZZLY 1 STE-JULIE", "grizzly 1 ste")]
    [InlineData("GRIZZLY 1 STE-JULIE", "grizzly 1 ste julie")]
    [InlineData("GRIZZLY 1 STE-JULIE", "grizzly 1 ste-julie")]
    [InlineData("GRIZZLY 1 STE-JULIE", "GRIZZLY")]
    public void Matches_AcceptsABaseNamePrefixOnAWordBoundary(string teamName, string baseName) =>
        Assert.True(TeamNameMatcher.Matches(teamName, baseName));

    [Theory]
    [InlineData("GRIZZLYS 1 STE-JULIE", "grizzly")]
    [InlineData("GRIZZLY 1 STE-JULIE", "grizzly 2")]
    [InlineData("GRIZZLY 1 STE-JULIE", "ste-julie")]
    [InlineData("GRIZZLY 1 STE-JULIE", "")]
    [InlineData("GRIZZLY 1 STE-JULIE", "   ")]
    [InlineData("", "grizzly")]
    [InlineData(null, "grizzly")]
    [InlineData("GRIZZLY 1 STE-JULIE", null)]
    public void Matches_RejectsANonPrefixOrAnUnknownName(string? teamName, string? baseName) =>
        Assert.False(TeamNameMatcher.Matches(teamName, baseName));

    [Fact]
    public void Normalize_RemovesAccentsCaseAndSeparators()
    {
        Assert.Equal("st julie", TeamNameMatcher.Normalize("St-Julie"));
        Assert.Equal("cote saint luc", TeamNameMatcher.Normalize("  Côté   Saint--Luc "));
        Assert.Equal("aigles st jerome", TeamNameMatcher.Normalize("AIGLES ST-JÉRÔME"));
        Assert.Equal(string.Empty, TeamNameMatcher.Normalize("  "));
        Assert.Equal(string.Empty, TeamNameMatcher.Normalize(null));
    }

    [Fact]
    public void Matches_IsAccentInsensitiveOnBothSides()
    {
        Assert.True(TeamNameMatcher.Matches("AIGLES ST-JÉRÔME", "aigles st-jerome"));
        Assert.True(TeamNameMatcher.Matches("AIGLES ST-JEROME", "aigles st-jérôme"));
    }
}
