using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HockeySeason.Functions;
using HockeySeason.LeagueApi;
using HockeySeason.LeagueApi.Http;
using HockeySeason.LeagueApi.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace HockeySeason.Tests;

/// <summary>
/// Coverage of the game detail endpoint of <see cref="LeagueApiService"/>:
/// the two requests it sends, the wire fields it maps and the failure it
/// raises for an unknown game.
/// </summary>
public sealed class LeagueApiGameDetailTests
{
    private const string ApiKey = "test-api-key";

    private const string GamePayload =
        """
        {
          "number": "3526", "date": "2026-10-23", "startTime": "2026-10-23T23:10:00.000Z",
          "timezone": "America/Toronto", "division": "U13", "seasonId": "2026-27",
          "homeTeamId": 181303, "awayTeamId": 181311, "isApproved": true, "isLive": false,
          "id": 942131,
          "teamStats": [
            {
              "teamId": 181303, "goalFor": 2, "goalAgainst": 4, "gameResult": "loss",
              "winLossType": "regulation", "pim": 4, "ppg": 0, "ppo": 3, "pk": 1, "pko": 2
            },
            {
              "teamId": 181311, "goalFor": 4, "goalAgainst": 2, "gameResult": "win",
              "winLossType": "regulation", "pim": 6, "ppg": 1, "ppo": 2, "pk": 3, "pko": 3
            }
          ],
          "homeTeam": { "name": "GRIZZLY 1 STE-JULIE", "id": 181303, "logoUrl": "https://example.test/grizzly.png" },
          "awayTeam": { "name": "MOUSQUETAIRES 2 ST-HYACINTHE", "id": 181311, "logoUrl": null }
        }
        """;

    private const string BoxScorePayload =
        """
        {
          "gameNumber": "3526",
          "teams": [
            { "id": 181303, "name": "GRIZZLY 1 STE-JULIE", "logoUrl": "https://example.test/grizzly.png" },
            { "id": 181311, "name": "MOUSQUETAIRES 2 ST-HYACINTHE", "logoUrl": null }
          ],
          "goals": [
            {
              "gameTime": { "period": "3", "minutes": 0, "seconds": 52, "elapsedMinutes": 9, "elapsedSeconds": 8 },
              "teamId": 181311,
              "participant": { "fullName": "JACK LAPRISE", "number": 24, "participantId": 1, "isAffiliate": false },
              "assists": [],
              "isPowerplay": false, "isShorthanded": false, "isEmptyNet": true,
              "isPenaltyShot": false, "isOwnGoal": null
            },
            {
              "gameTime": { "period": "1", "minutes": 2, "seconds": 33, "elapsedMinutes": 7, "elapsedSeconds": 27 },
              "teamId": 181303,
              "participant": { "fullName": "LOUIS-THOMAS DESMARCHAIS", "number": 51, "participantId": 2, "isAffiliate": false },
              "assists": [
                { "fullName": "THOMAS BELOIN", "number": 53, "participantId": 3, "isAffiliate": false }
              ],
              "isPowerplay": true, "isShorthanded": false, "isEmptyNet": false,
              "isPenaltyShot": false, "isOwnGoal": null
            }
          ],
          "shootout": [],
          "penalties": [
            {
              "gameTime": { "period": "2", "minutes": 8, "seconds": 52, "elapsedMinutes": 1, "elapsedSeconds": 8 },
              "teamId": 181311,
              "infraction": "Interference",
              "duration": { "name": "Minor" },
              "participant": { "fullName": "MATHYS ST-ONGE", "number": 12, "participantId": 4, "isAffiliate": true },
              "isBenchPenalty": false,
              "i18n": { "fr": { "infraction": "Obstruction" } }
            }
          ]
        }
        """;

    /// <summary>A game that has not been played: the league omits every scored member.</summary>
    private const string UnplayedGamePayload =
        """
        {
          "number": "3591", "date": "2026-12-06", "startTime": "2026-12-06T20:30:00.000Z",
          "timezone": "America/Toronto", "division": "U13", "seasonId": "2026-27",
          "homeTeamId": 181303, "awayTeamId": 181309, "isApproved": false, "isLive": false,
          "id": 942067,
          "homeTeam": { "name": "GRIZZLY 1 STE-JULIE", "id": 181303, "logoUrl": null },
          "awayTeam": { "name": "AIGLES ST-BRUNO", "id": 181309, "logoUrl": null }
        }
        """;

    [Fact]
    public async Task GetLeagueGameDetail_RequestsTheGameAndItsBoxScore()
    {
        var handler = new RoutingHandler();
        var service = CreateService(handler);

        await service.GetLeagueGameDetailAsync(942131);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("/api/sp/games/942131", handler.Requests[0].Uri.AbsolutePath);
        Assert.Equal("/api/sp/games/942131/boxScore", handler.Requests[1].Uri.AbsolutePath);
        Assert.Equal(
            """{"include":["awayTeam","homeTeam","teamStats"]}""",
            Uri.UnescapeDataString(handler.Requests[0].Uri.Query.TrimStart('?')["filter=".Length..]));
    }

    [Fact]
    public async Task GetLeagueGameDetail_SendsTheLeagueHeaders()
    {
        var handler = new RoutingHandler();
        var service = CreateService(handler);

        await service.GetLeagueGameDetailAsync(942131);

        var headers = handler.Requests[0].Headers;
        Assert.Equal(ApiKey, headers["authorization"].Single()["API-Key ".Length..]);
        Assert.Equal("LEAGUE", headers["x-page-type"].Single());
        Assert.Equal("fr;en", headers["accept-language"].Single());
    }

    [Fact]
    public async Task GetLeagueGameDetail_MapsGoalsInGameOrder()
    {
        var handler = new RoutingHandler();
        var service = CreateService(handler);

        var detail = await service.GetLeagueGameDetailAsync(942131);

        Assert.Equal(942131, detail.Id);
        Assert.Equal("3526", detail.Number);
        // A 23:10Z start is 19:10 in Toronto.
        Assert.Equal("19:10", detail.Time);
        Assert.Equal("U13", detail.Division);

        Assert.Equal(2, detail.Goals.Count);
        Assert.Equal(
            new[] { (181303, 1, 1), (181311, 3, 1) },
            detail.PeriodGoals.Select(goals => (goals.TeamId, goals.Period, goals.Goals)));

        // The box score lists the third-period goal first; the detail orders by clock.
        var opener = detail.Goals[0];
        Assert.Equal(1, opener.Period);
        Assert.Equal("7:27", opener.Time);
        Assert.Equal(181303, opener.TeamId);
        Assert.Equal("LOUIS-THOMAS DESMARCHAIS", opener.Scorer.Name);
        Assert.Equal("THOMAS BELOIN", Assert.Single(opener.Assists).Name);
        Assert.True(opener.IsPowerPlay);

        Assert.Equal(3, detail.Goals[1].Period);
        Assert.True(detail.Goals[1].IsEmptyNet);
    }

    [Fact]
    public async Task GetLeagueGameDetail_UsesTheLocalizedInfractionAndMapsTeamStats()
    {
        var handler = new RoutingHandler();
        var service = CreateService(handler);

        var detail = await service.GetLeagueGameDetailAsync(942131);

        var penalty = Assert.Single(detail.Penalties);
        Assert.Equal("Obstruction", penalty.Infraction);
        Assert.Equal("Minor", penalty.Duration);
        Assert.True(penalty.Player.IsAffiliate);

        Assert.Equal(2, detail.TeamStats.Count);
        var home = detail.TeamStats.Single(stats => stats.TeamId == 181303);
        Assert.Equal(2, home.GoalsFor);
        Assert.Equal(4, home.GoalsAgainst);
        Assert.Equal("loss", home.Result);
        Assert.Equal("0/3", home.PowerPlay);
        Assert.Equal("1/2", home.PenaltyKill);
    }

    [Fact]
    public async Task GetLeagueGameDetail_CountsEveryGoalByTeamAndPeriodAndSortsTimesNumerically()
    {
        var boxScore = JsonNode.Parse(BoxScorePayload)!.AsObject();
        var goals = boxScore["goals"]!.AsArray();
        var homeGoal = goals[1]!.DeepClone();
        homeGoal["gameTime"]!["elapsedMinutes"] = 10;
        homeGoal["gameTime"]!["elapsedSeconds"] = 0;
        goals.Add(homeGoal);
        var awayGoal = goals[0]!.DeepClone();
        awayGoal["gameTime"]!["period"] = "4";
        goals.Add(awayGoal);
        var handler = new RoutingHandler { BoxScorePayload = boxScore.ToJsonString() };

        var detail = await CreateService(handler).GetLeagueGameDetailAsync(942131);

        Assert.Equal(new[] { "7:27", "10:00", "9:08", "9:08" }, detail.Goals.Select(goal => goal.Time));
        Assert.Equal(
            new[] { (181303, 1, 2), (181311, 3, 1), (181311, 4, 1) },
            detail.PeriodGoals.Select(goals => (goals.TeamId, goals.Period, goals.Goals)));
    }

    [Fact]
    public async Task GetLeagueGameDetail_MapsAnUnplayedGameToNoGoalsAndNoStats()
    {
        var handler = new RoutingHandler
        {
            GamePayload = UnplayedGamePayload,
            BoxScorePayload = """{"gameNumber":"3591","teams":[],"goals":[],"shootout":[],"penalties":[]}""",
        };
        var service = CreateService(handler);

        var detail = await service.GetLeagueGameDetailAsync(942067);

        Assert.Empty(detail.Goals);
        Assert.Empty(detail.PeriodGoals);
        Assert.Empty(detail.Penalties);
        Assert.Empty(detail.TeamStats);
        Assert.False(detail.IsLive);
        Assert.False(detail.IsApproved);
    }

    [Fact]
    public async Task GameDetailFunction_ReturnsCompleteGoalsAndPeriodCounts()
    {
        var handler = new RoutingHandler();
        var function = new GameDetailFunction(CreateService(handler));
        var request = new DefaultHttpContext().Request;

        Assert.IsType<BadRequestObjectResult>(await function.Run(request, 0, CancellationToken.None));
        Assert.Empty(handler.Requests);
        var response = Assert.IsType<OkObjectResult>(await function.Run(request, 942131, CancellationToken.None));
        var detail = Assert.IsType<LeagueGameDetail>(response.Value);
        Assert.Equal(2, detail.Goals.Count);
        Assert.Equal("LOUIS-THOMAS DESMARCHAIS", detail.Goals[0].Scorer.Name);
        Assert.Equal("THOMAS BELOIN", Assert.Single(detail.Goals[0].Assists).Name);
        Assert.Equal(2, detail.PeriodGoals.Sum(goal => goal.Goals));
    }

    [Fact]
    public async Task GameDetailEndpoint_ReturnsAllEventsAndCalculatedPeriodsAsJson()
    {
        var handler = new RoutingHandler();
        await using var app = new WebApplicationFactory<HockeySeason.Api.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Auth:Authority", "https://tests.hockey-season-data");
            builder.UseSetting("Auth:Issuer", "https://tests.hockey-season-data");
            builder.UseSetting("Auth:Audience", "hockey-season-api-tests");
            builder.UseSetting("Auth:AllowHttpAuthority", "true");
            builder.UseSetting("LeagueApi:ApiKey", "test-league-key");
            builder.ConfigureServices(services => services.AddSingleton(CreateService(handler)));
        });
        var client = app.CreateClient();

        var invalid = await client.GetAsync("/api/games/0");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Empty(handler.Requests);

        var response = await client.GetAsync("/api/games/942131");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal(942131, root.GetProperty("id").GetInt32());
        Assert.Equal(2, root.GetProperty("goals").GetArrayLength());
        Assert.Equal("LOUIS-THOMAS DESMARCHAIS", root.GetProperty("goals")[0].GetProperty("scorer").GetProperty("name").GetString());
        Assert.Equal("THOMAS BELOIN", root.GetProperty("goals")[0].GetProperty("assists")[0].GetProperty("name").GetString());
        Assert.Equal(1, root.GetProperty("penalties").GetArrayLength());
        Assert.Equal(2, root.GetProperty("periodGoals").GetArrayLength());
        Assert.Equal(1, root.GetProperty("periodGoals")[0].GetProperty("goals").GetInt32());
    }

    [Fact]
    public async Task GetLeagueGameDetail_RejectsANonPositiveGameId()
    {
        var handler = new RoutingHandler();
        var service = CreateService(handler);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await service.GetLeagueGameDetailAsync(0));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetLeagueGameDetail_ThrowsWhenTheApiIsUnavailable()
    {
        var handler = new RoutingHandler { GameStatus = HttpStatusCode.ServiceUnavailable };
        var service = CreateService(handler);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            async () => await service.GetLeagueGameDetailAsync(942131));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
    }

    private static LeagueApiService CreateService(HttpMessageHandler handler)
    {
        var options = new LeagueApiOptions
        {
            BaseAddress = new("https://pub-api.play.spordle.com/api/"),
            ApiKey = ApiKey,
        };

        var httpClient = new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = options.BaseAddress,
        };

        return new LeagueApiService(
            httpClient,
            Options.Create(options),
            NullLogger<LeagueApiService>.Instance);
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        Uri Uri,
        Dictionary<string, string[]> Headers);

    private sealed class RoutingHandler : HttpMessageHandler
    {
        public string GamePayload { get; init; } = LeagueApiGameDetailTests.GamePayload;

        public string BoxScorePayload { get; init; } = LeagueApiGameDetailTests.BoxScorePayload;

        public HttpStatusCode GameStatus { get; init; } = HttpStatusCode.OK;

        public List<CapturedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.ToDictionary(
                    header => header.Key,
                    header => header.Value.ToArray(),
                    StringComparer.OrdinalIgnoreCase)));

            var path = request.RequestUri!.AbsolutePath;

            var (body, status) = path.EndsWith("/boxScore", StringComparison.Ordinal)
                ? (BoxScorePayload, HttpStatusCode.OK)
                : (GamePayload, GameStatus);

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
