using HockeySeason.Application;
using Xunit;

namespace HockeySeason.Tests;

/// <summary>
/// The category level of a league, read from its published French name: a
/// director follows "M13" as a whole, so "M13 A", "M13 B" and "M13 AA" must all
/// resolve to the same level while an unrelated category never matches.
/// </summary>
public sealed class LeagueCategoryLevelTests
{
    [Theory]
    [InlineData("M11 A", "M11")]
    [InlineData("M13 B", "M13")]
    [InlineData("M13 AA", "M13")]
    [InlineData("M13A", "M13")]
    [InlineData("M15", "M15")]
    [InlineData("M17 AAA", "M17")]
    [InlineData("M19 BB", "M19")]
    [InlineData("Junior A", LeagueCategoryLevel.Junior)]
    [InlineData("JUVÉNIL B", LeagueCategoryLevel.Junior)]
    [InlineData("  m13   b  ", "M13")]
    public void FromLeagueName_ReadsTheLevelWhateverTheLetter(string leagueName, string expected) =>
        Assert.Equal(expected, LeagueCategoryLevel.FromLeagueName(leagueName));

    [Theory]
    [InlineData("Atome BB")]
    [InlineData("M")]
    [InlineData("M B")]
    [InlineData("")]
    [InlineData(null)]
    public void FromLeagueName_ReturnsNullWithoutALevel(string? leagueName) =>
        Assert.Null(LeagueCategoryLevel.FromLeagueName(leagueName));

    [Theory]
    [InlineData("M13 B", "M13")]
    [InlineData("M13 AA", "m13")]
    [InlineData("M13", " M13 ")]
    [InlineData("Junior A", "junior")]
    public void Matches_FoldsTheLetterIntoTheCategory(string leagueName, string category) =>
        Assert.True(LeagueCategoryLevel.Matches(leagueName, category));

    [Theory]
    [InlineData("M15 A", "M13")]
    [InlineData("M13 B", "M11")]
    [InlineData("Junior A", "M19")]
    [InlineData("Atome BB", "M13")]
    [InlineData("M13 B", "")]
    public void Matches_RejectsAnotherCategory(string leagueName, string category) =>
        Assert.False(LeagueCategoryLevel.Matches(leagueName, category));

    [Fact]
    public void Comparer_ReadsTheLevelsInAgeOrder()
    {
        string[] shuffled = ["Junior", "M17", "M11", "M19", "M13", "M15"];
        // Ordering canonicalizes the levels, but keeps the original items.
        Assert.Equal(
            ["M11", "M13", "M15", "M17", "M19", "Junior"],
            shuffled.OrderBy(level => level, LeagueCategoryLevel.Comparer));
    }

    [Fact]
    public void Display_ReadsJuniorInLowercase()
    {
        Assert.Equal("M13", LeagueCategoryLevel.Display("M13"));
        Assert.Equal("Junior", LeagueCategoryLevel.Display(LeagueCategoryLevel.Junior));
    }
}
