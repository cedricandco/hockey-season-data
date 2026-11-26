using System.Text.Json;
using HockeySeason.Application;
using HockeySeason.Functions;
using HockeySeason.LeagueApi;
using HockeySeason.LeagueApi.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace HockeySeason.Tests;

/// <summary>
/// The landing-page preview is the only anonymous surface of the API: it reads
/// the imported reference data and the public schedule of a team without a
/// token, and returns database ids so the shared statistics view renders the
/// same figures as the authenticated team page.
/// </summary>
public sealed class PreviewFunctionsTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Selectors_AreReadableWithoutAToken()
    {
        // A request with no Authorization header: the preview triggers do not
        // consult FunctionsAuthorization at all.
        var function = CreatePreview();
        var request = new DefaultHttpContext().Request;

        using var associations = await ReadJsonAsync(function.Associations(request, CancellationToken.None));
        Assert.Equal("Hockey Richelieu",
            associations.RootElement.GetProperty("associations")[0].GetProperty("name").GetString());

        using var divisions = await ReadJsonAsync(function.Divisions(request, 1, CancellationToken.None));
        Assert.Equal(6193, divisions.RootElement.GetProperty("divisions")[0].GetProperty("id").GetInt32());

        using var leagues = await ReadJsonAsync(function.Leagues(request, 6193, CancellationToken.None));
        Assert.Equal("2026-27", leagues.RootElement.GetProperty("seasonId").GetString());

        // The team list is keyed on the database id the detail call expects.
        using var teams = await ReadJsonAsync(function.Teams(request, "category-guid", CancellationToken.None));
        var team = teams.RootElement.GetProperty("teams")[0];
        Assert.Equal(500, team.GetProperty("id").GetInt32());
        Assert.Equal("Grizzly 1", team.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Selectors_RejectNegativeIdentifiers()
    {
        var function = CreatePreview();
        var request = new DefaultHttpContext().Request;

        Assert.IsType<BadRequestObjectResult>(await function.Divisions(request, 0, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await function.Leagues(request, -1, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await function.Teams(request, "  ", CancellationToken.None));
    }

    [Fact]
    public async Task TeamDetail_RendersTheTeamPageShapeWithTheLeagueMapping()
    {
        var details = new RecordingTeamDetails();
        var function = CreatePreview(details);
        var request = new DefaultHttpContext().Request;

        var result = Assert.IsType<OkObjectResult>(await function.TeamDetail(request, 500, CancellationToken.None));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value, Web));

        // The stored schedule is resolved from the database, never typed by the
        // visitor, and the detail is read with the Spordle team id.
        Assert.Equal([194908], details.RequestedSchedules);
        Assert.Equal(181303, details.RequestedTeam);
        Assert.Equal("Grizzly 1", json.RootElement.GetProperty("team").GetProperty("teamName").GetString());

        // teamLinks carry the Spordle → database mapping of the league teams.
        var links = json.RootElement.GetProperty("teamLinks").EnumerateArray()
            .Select(link => (External: link.GetProperty("externalTeamId").GetInt32(), Page: link.GetProperty("pageTeamId").GetInt32()))
            .ToArray();
        Assert.Equal([(181303, 500), (181305, 502)], links);
    }

    [Fact]
    public async Task TeamDetail_Is404ForATeamThePreviewCannotOpen()
    {
        var function = CreatePreview();
        var request = new DefaultHttpContext().Request;

        Assert.IsType<NotFoundResult>(await function.TeamDetail(request, 999, CancellationToken.None));
    }

    [Fact]
    public async Task WithoutPersistence_ThePreviewListsNothing()
    {
        // A database-less host cannot resolve the reference data: the landing
        // page gets empty lists instead of an error.
        var function = new PreviewFunctions(
            new FakeCatalog(), new FakePreview(), new RecordingTeamDetails(), FunctionsTests.WithoutPersistence());
        var request = new DefaultHttpContext().Request;

        using var teams = await ReadJsonAsync(function.Teams(request, "category-guid", CancellationToken.None));
        Assert.Empty(teams.RootElement.GetProperty("teams").EnumerateArray());
        Assert.IsType<NotFoundResult>(await function.TeamDetail(request, 500, CancellationToken.None));
    }

    private static PreviewFunctions CreatePreview(RecordingTeamDetails? details = null) =>
        new(new FakeCatalog(), new FakePreview(), details ?? new RecordingTeamDetails(),
            FunctionsTests.WithTestDatabase());

    private static async Task<JsonDocument> ReadJsonAsync(Task<IActionResult> action)
    {
        var result = Assert.IsType<OkObjectResult>(await action);
        return JsonDocument.Parse(JsonSerializer.Serialize(result.Value, Web));
    }

    private sealed class FakeCatalog : ISubscriptionCatalogService
    {
        public Task<IReadOnlyList<SubscriptionAssociation>> GetAssociationsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SubscriptionAssociation>>([new(1, "Hockey Richelieu")]);

        public Task<IReadOnlyList<SubscriptionDivision>> GetDivisionsAsync(int associationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SubscriptionDivision>>([new(6193, "Simple lettre")]);

        public Task<SubscriptionLeagueOptions> GetLeaguesAsync(int divisionId, string? seasonId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SubscriptionLeagueOptions("2026-27", [new("category-guid", "M11A", "2026-27")]));

        public Task<IReadOnlyList<SubscriptionTeam>> GetTeamsAsync(string leagueId, int? divisionId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SubscriptionTeam>>([]);

        public Task<PresidentTeamOptions> GetTeamsByBaseNameAsync(int associationId, string baseName, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PresidentTeamOptions(associationId, "Hockey Richelieu", baseName, []));

        public Task<IReadOnlyList<DirectorCategory>> GetCategoriesAsync(int associationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DirectorCategory>>([]);

        public Task<DirectorCategoryOptions> GetTeamsByCategoryAsync(int associationId, string category, string? baseName = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DirectorCategoryOptions(associationId, "Hockey Richelieu", category, baseName, []));
    }

    private sealed class FakePreview : IPreviewService
    {
        public Task<IReadOnlyList<PreviewTeam>> GetTeamsAsync(string leagueId, int? divisionId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PreviewTeam>>([new(500, "Grizzly 1")]);

        public Task<SelectableTeam?> GetTeamAsync(int teamId, CancellationToken cancellationToken = default) =>
            Task.FromResult<SelectableTeam?>(teamId == 500
                ? new SelectableTeam(500, 181303, "Grizzly 1", 100, "M11A",
                    [new FollowedSchedule(194908, "M11A Saison")])
                : null);

        public Task<IReadOnlyList<PreviewTeamLink>> GetLeagueTeamLinksAsync(int leagueId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PreviewTeamLink>>([new(500, 181303), new(502, 181305)]);
    }

    /// <summary>Records the schedule ids the preview resolved from the database.</summary>
    private sealed class RecordingTeamDetails : ITeamDetailService
    {
        public List<int> RequestedSchedules { get; } = [];
        public int? RequestedTeam { get; private set; }

        public Task<TeamDetailResponse?> GetAsync(int leagueId, int teamId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The preview route must resolve the schedule, not use a Spordle league id.");

        public Task<TeamDetailResponse?> GetAsync(
            IReadOnlyList<int> scheduleIds, int teamId, CancellationToken cancellationToken)
        {
            RequestedSchedules.AddRange(scheduleIds);
            RequestedTeam = teamId;
            var standing = new LeagueStanding(
                teamId, "Grizzly 1", null, "M11A", "M", 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, null, null);
            return Task.FromResult<TeamDetailResponse?>(new TeamDetailResponse(
                194908, standing, [standing], [], [], [], 0, 0, 0, [], [], 0, 0, 0, false));
        }
    }
}
