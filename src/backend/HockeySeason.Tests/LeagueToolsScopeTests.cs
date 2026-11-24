using HockeySeason.Application;
using HockeySeason.Infrastructure.Data;
using HockeySeason.Mcp;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using Xunit;

namespace HockeySeason.Tests;

/// <summary>
/// The MCP tools read the reference data from the database, limited to the
/// caller's follows, and still call Spordle for the live figures. These tests
/// cover the "only what I follow" rule: the listing is the followed catalogue,
/// and asking about another league, schedule or team is refused.
/// </summary>
public sealed class LeagueToolsScopeTests
{
    private static CallerDataScope FollowedScope() => new(
        true,
        [new ScopedAssociation(1, "Hockey Richelieu")],
        [new ScopedDivision(6193, "Simple lettre", 1)],
        [new ScopedLeague(1, "league-guid", "M13 B", "2026-27", 6193, [194899])],
        [new ScopedTeam(182497, "Grizzlys", "league-guid", 194899)]);

    private static LeagueTools CreateTools(CallerDataScope scope, string? subject = "auth0|coach")
    {
        var httpContext = new DefaultHttpContext();
        if (subject is not null)
        {
            httpContext.User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity(
                    [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, subject)],
                    authenticationType: "Bearer"));
        }

        // Real collaborators over a handler that fails if it is ever reached:
        // the reference tools must not touch Spordle at all.
        var options = Options.Create(new HockeySeason.LeagueApi.LeagueApiOptions { ApiKey = "test" });
        var league = new HockeySeason.LeagueApi.Http.LeagueApiService(
            new HttpClient(new UnreachableHandler()), options,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<HockeySeason.LeagueApi.Http.LeagueApiService>.Instance);
        return new LeagueTools(
            league,
            new HockeySeason.LeagueApi.TeamDetailService(league),
            new StubScope(scope),
            new CallerIdentity(new HttpContextAccessor { HttpContext = httpContext },
                Options.Create(new AuthorizationOptions())));
    }

    /// <summary>Handler that fails the test if a Spordle request is attempted.</summary>
    private sealed class UnreachableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                $"No Spordle call is expected for '{request.RequestUri}', but the tool contacted it.");
    }

    [Fact]
    public async Task ReferenceTools_ListOnlyTheCallerFollows()
    {
        var tools = CreateTools(FollowedScope());

        var associations = await tools.GetAssociations(CancellationToken.None);
        Assert.Equal([1], associations.Select(association => association.Id));

        var divisions = await tools.GetDivisions(cancellationToken: CancellationToken.None);
        Assert.Equal([6193], divisions.Select(division => division.Id));
        Assert.Empty(await tools.GetDivisions(associationId: 999, cancellationToken: CancellationToken.None));

        var leagues = await tools.GetLeagues(cancellationToken: CancellationToken.None);
        var league = Assert.Single(leagues);
        Assert.Equal("league-guid", league.ExternalId);
        Assert.Equal([194899], league.ScheduleIds);

        var teams = await tools.GetLeagueTeams("league-guid", CancellationToken.None);
        Assert.Equal([182497], teams.Select(team => team.Id));
    }

    [Fact]
    public async Task ReferenceTools_RefuseALeagueOutsideTheFollows()
    {
        var tools = CreateTools(FollowedScope());

        var exception = await Assert.ThrowsAsync<McpException>(
            () => tools.GetLeagueTeams("other-league", CancellationToken.None));
        Assert.Contains("not one of the leagues you follow", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LiveTools_RefuseAScheduleOutsideTheFollowsBeforeCallingSpordle()
    {
        var tools = CreateTools(FollowedScope());

        // The validation runs before the Spordle client is touched: the null
        // LeagueApiService is never dereferenced.
        var standings = await Assert.ThrowsAsync<McpException>(
            () => tools.GetLeagueStanding(999, CancellationToken.None));
        Assert.Contains("not one of the schedules you follow", standings.Message, StringComparison.OrdinalIgnoreCase);

        var suspensions = await Assert.ThrowsAsync<McpException>(
            () => tools.GetLeagueSuspension(999, CancellationToken.None));
        Assert.Contains("not one of the schedules you follow", suspensions.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TeamDetail_RefusesATeamOutsideTheFollows()
    {
        var tools = CreateTools(FollowedScope());

        var exception = await Assert.ThrowsAsync<McpException>(
            () => tools.GetTeamDetail(999999, CancellationToken.None));
        Assert.Contains("not one of the teams you may read", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NothingFollowed_ListsNothing()
    {
        var tools = CreateTools(CallerDataScope.NothingFollowed());

        Assert.Empty(await tools.GetAssociations(CancellationToken.None));
        Assert.Empty(await tools.GetDivisions(cancellationToken: CancellationToken.None));
        Assert.Empty(await tools.GetLeagues(cancellationToken: CancellationToken.None));
        await Assert.ThrowsAsync<McpException>(() => tools.GetLeagueTeams("league-guid", CancellationToken.None));
    }

    /// <summary>Data scope of the caller, as the database implementation returns it.</summary>
    private sealed class StubScope(CallerDataScope scope) : ICallerDataScopeService
    {
        public Task<CallerDataScope> GetAsync(int userId, bool unrestricted = false, CancellationToken cancellationToken = default) =>
            Task.FromResult(scope);

        public Task<CallerDataScope> GetForSubjectAsync(string? auth0Subject, bool unrestricted = false, CancellationToken cancellationToken = default) =>
            Task.FromResult(scope);

        public Task<IReadOnlyList<FollowedTeam>> GetFollowedTeamsAsync(
            int userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FollowedTeam>>(
                [new FollowedTeam(182497, 182497, "Grizzlys", 1, "M13 B", [new FollowedSchedule(194899, "M13 B Saison")], 6193, "Simple lettre", 1, "Hockey Richelieu")]);

        public Task<IReadOnlyList<SelectableTeam>> GetLeagueTeamsAsync(
            int userId, int leagueId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SelectableTeam>>([]);

        public Task<SelectableTeam?> GetSelectableTeamAsync(
            int userId, int teamId, CancellationToken cancellationToken = default) =>
            Task.FromResult<SelectableTeam?>(null);
    }
}
