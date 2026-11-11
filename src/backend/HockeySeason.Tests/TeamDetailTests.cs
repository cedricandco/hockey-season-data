using System.Net;
using System.Text;
using HockeySeason.LeagueApi;
using HockeySeason.LeagueApi.Http;
using HockeySeason.Functions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HockeySeason.Tests;

public sealed class TeamDetailTests
{
    private readonly OidcTestServer _identityServerForTests = new();

    [Fact]
    public async Task TeamDetail_AggregatesOnlyTeamEventsAndKeepsFullGames()
    {
        var handler = new TeamHandler();
        var service = CreateService(handler);
        var result = await service.GetAsync(100, 1, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Single(result.Games);
        Assert.Equal(10, result.Games[0].Detail.Id);
        Assert.Single(result.UpcomingGames);
        Assert.Equal(11, result.UpcomingGames[0].Id);
        Assert.Equal(1, result.TotalGoals);
        Assert.Equal(2, result.TotalAssists);
        Assert.Equal(3, result.TotalPoints);
        Assert.Equal(101, Assert.Single(result.TopScorers).ParticipantId);
        Assert.Equal(new[] { 102, 103 }, result.TopAssistProviders.Select(player => player.ParticipantId));
        Assert.Equal(1, result.PowerPlayGoals);
        Assert.Equal(0, result.ShorthandedGoals);
        Assert.Equal(1, result.Penalties);
        Assert.Equal(new[] { 101, 102, 103, 104 }, result.Players.Select(p => p.ParticipantId));
        Assert.Equal((1, 0, 1), (result.Players[0].Goals, result.Players[0].Assists, result.Players[0].Points));
        Assert.False(result.CompleteRosterAvailable);
        Assert.Equal(2, result.Standings.Count);
        Assert.DoesNotContain("between", handler.GamesQuery);
    }

    [Fact]
    public async Task TeamDetail_UnknownTeamReturnsNullWithoutFetchingGames()
    {
        var handler = new TeamHandler();
        var result = await CreateService(handler).GetAsync(100, 999, CancellationToken.None);
        Assert.Null(result);
        Assert.Null(handler.GamesQuery);
    }

    [Fact]
    public async Task TeamEndpoints_ReturnDetailAndValidateIds()
    {
        var handler = new TeamHandler();
        var league = CreateLeague(handler);
        await using var app = new WebApplicationFactory<HockeySeason.Api.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Auth:Authority", _identityServerForTests.Authority);
            builder.UseSetting("Auth:Issuer", _identityServerForTests.Issuer);
            builder.UseSetting("Auth:Audience", _identityServerForTests.Audience);
            builder.UseSetting("Auth:AllowHttpAuthority", "true");
            builder.UseSetting("LeagueApi:ApiKey", "test");
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(league);
                services.AddSingleton<ITeamDetailService>(new TeamDetailService(league));
            });
        });
        var client = app.CreateClient();
        var token = _identityServerForTests.CreateToken();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/offices")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/offices/6193/leagues")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/leagues/100/teams")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/leagues/0/teams")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/leagues/100/teams/0")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/leagues/100/teams/999")).StatusCode);
        var response = await client.GetAsync("/api/leagues/100/teams/1");
        response.EnsureSuccessStatusCode();
        using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(3, json.RootElement.GetProperty("totalPoints").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("topAssistProviders").GetArrayLength());
    }

    [Fact]
    public void FunctionsAuthorization_RequiresHttpContext()
    {
        var context = new FunctionsTests.FunctionContextStubPublicForTests();
        Assert.Throws<InvalidOperationException>(() => HockeySeason.Functions.FunctionsAuthorization.IsAuthenticated(context));
        Assert.Throws<InvalidOperationException>(() => HockeySeason.Functions.FunctionsAuthorization.IsAuthenticatedV2(context));
    }

    [Fact]
    public async Task Functions_ExposeSameDiscoveryAndTeamDetail()
    {
        var league = CreateLeague(new TeamHandler());
        var function = new TeamFunctions(league, new TeamDetailService(league), Options.Create(new LeagueApiOptions()));
        var request = new DefaultHttpContext().Request;
        var authContext = FunctionsTests.CreateFunctionContextForRequest(request);
        Assert.IsType<OkObjectResult>(function.Offices(request, authContext));
        Assert.IsType<OkObjectResult>(await function.Leagues(request, 6193, authContext, CancellationToken.None));
        Assert.IsType<OkObjectResult>(await function.LeagueTeams(request, 100, authContext, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await function.TeamDetail(request, 100, 0, authContext, CancellationToken.None));
        Assert.IsType<NotFoundResult>(await function.TeamDetail(request, 100, 999, authContext, CancellationToken.None));
        var result = Assert.IsType<OkObjectResult>(await function.TeamDetail(request, 100, 1, authContext, CancellationToken.None));
        Assert.Equal(3, Assert.IsType<TeamDetailResponse>(result.Value).TotalPoints);
    }

    private static TeamDetailService CreateService(TeamHandler handler)
        => new(CreateLeague(handler));

    private static LeagueApiService CreateLeague(TeamHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/api/") };
        var options = Options.Create(new LeagueApiOptions { BaseAddress = client.BaseAddress, ApiKey = "test" });
        return new LeagueApiService(client, options, NullLogger<LeagueApiService>.Instance);
    }

    private sealed class TeamHandler : HttpMessageHandler
    {
        public string? GamesQuery { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            string payload;
            if (path.EndsWith("/schedules", StringComparison.Ordinal))
                payload = """[{"id":100,"name":"M13"}]""";
            else if (path.EndsWith("/teamStats", StringComparison.Ordinal))
                payload = """
                    [
                      {"teamId":1,"ranking":1,"gamesPlayed":1,"wins":1,"losses":0,"ties":0,"points":2,"goalFor":1,"goalAgainst":0,"diff":1,"pim":2,"powerplayPct":50,"penaltyKillPct":100,"team":{"id":1,"name":"Alpha","shortName":"A","division":"M13","gender":"M"}},
                      {"teamId":2,"ranking":2,"gamesPlayed":1,"wins":0,"losses":1,"ties":0,"points":0,"goalFor":0,"goalAgainst":1,"diff":-1,"pim":0,"powerplayPct":0,"penaltyKillPct":50,"team":{"id":2,"name":"Beta","shortName":"B","division":"M13","gender":"M"}}
                    ]
                    """;
            else if (path.EndsWith("/games", StringComparison.Ordinal))
            {
                GamesQuery = Uri.UnescapeDataString(request.RequestUri.Query);
                payload = """
                    [
                      {"id":10,"date":"2020-01-01","startTime":"2020-01-01T18:00:00Z","timeZone":"America/Toronto","homeTeamId":1,"awayTeamId":2,"homeTeam":{"id":1,"name":"Alpha"},"awayTeam":{"id":2,"name":"Beta"},"score":{"1":1,"2":0}},
                      {"id":12,"date":"2020-01-02","startTime":"2020-01-02T18:00:00Z","timeZone":"America/Toronto","homeTeamId":1,"awayTeamId":2,"homeTeam":{"id":1,"name":"Alpha"},"awayTeam":{"id":2,"name":"Beta"},"score":{"1":1,"2":0},"isLive":true},
                      {"id":11,"date":"2099-01-01","startTime":"2099-01-01T18:00:00Z","timeZone":"America/Toronto","homeTeamId":2,"awayTeamId":1,"homeTeam":{"id":2,"name":"Beta"},"awayTeam":{"id":1,"name":"Alpha"}}
                    ]
                    """;
            }
            else if (path.EndsWith("/boxScore", StringComparison.Ordinal))
                payload = """
                    {"gameNumber":"1","goals":[
                      {"gameTime":{"period":"1","elapsedMinutes":1,"elapsedSeconds":0},"teamId":1,
                       "participant":{"fullName":"A","number":1,"participantId":101,"isAffiliate":false},
                       "assists":[{"fullName":"B","number":2,"participantId":102,"isAffiliate":false},{"fullName":"C","number":3,"participantId":103,"isAffiliate":false}],
                       "isPowerplay":true,"isShorthanded":false,"isEmptyNet":false,"isPenaltyShot":false},
                      {"gameTime":{"period":"2","elapsedMinutes":1,"elapsedSeconds":0},"teamId":2,
                       "participant":{"fullName":"Opponent","number":1,"participantId":200,"isAffiliate":false},
                       "assists":[],"isPowerplay":false,"isShorthanded":true,"isEmptyNet":false,"isPenaltyShot":false}
                     ],"penalties":[
                      {"gameTime":{"period":"2","elapsedMinutes":2,"elapsedSeconds":0},"teamId":1,"infraction":"Tripping","duration":{"name":"Minor"},
                       "participant":{"fullName":"D","number":4,"participantId":104,"isAffiliate":false},"isBenchPenalty":false}
                     ]}
                    """;
            else if (path.EndsWith("/games/10", StringComparison.Ordinal))
                payload = """
                    {"id":10,"number":"1","date":"2020-01-01","startTime":"2020-01-01T18:00:00Z","timeZone":"America/Toronto","division":"M13","seasonId":"2019-20","isApproved":true,
                     "homeTeam":{"id":1,"name":"Alpha"},"awayTeam":{"id":2,"name":"Beta"},"teamStats":[]}
                    """;
            else return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload, Encoding.UTF8, "application/json") });
        }
    }
}
