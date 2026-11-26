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

    [Fact]
    public void BaseNameOf_ReturnsTheFirstWordOfThePublishedName()
    {
        Assert.Equal("grizzly", TeamNameMatcher.BaseNameOf("GRIZZLY 1 STE-JULIE"));
        Assert.Equal("aigles", TeamNameMatcher.BaseNameOf("AIGLES ST-JÉRÔME"));
        Assert.Equal("grizzly", TeamNameMatcher.BaseNameOf("  GRIZZLY--2  "));
        Assert.Equal(string.Empty, TeamNameMatcher.BaseNameOf(""));
        Assert.Equal(string.Empty, TeamNameMatcher.BaseNameOf(null));
    }

    /// <summary>
    /// The category director keeps one name per level, keyed on
    /// <see cref="TeamNameMatcher.BaseNameOf"/>: every team of that name matches
    /// the key, and teams of another name do not.
    /// </summary>
    [Fact]
    public void BaseNameOf_GroupsEveryTeamOfTheSameName()
    {
        Assert.Equal(
            TeamNameMatcher.BaseNameOf("GRIZZLY 1 STE-JULIE"),
            TeamNameMatcher.BaseNameOf("GRIZZLY 2 ST-HYACINTHE"));
        Assert.NotEqual(
            TeamNameMatcher.BaseNameOf("GRIZZLY 1 STE-JULIE"),
            TeamNameMatcher.BaseNameOf("MOUSQUETAIRE 1 ST-JEAN"));
    }
}
