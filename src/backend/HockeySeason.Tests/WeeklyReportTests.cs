using System.Collections.Concurrent;
using System.Net;
using System.Text;
using HockeySeason.LeagueApi;
using HockeySeason.LeagueApi.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace HockeySeason.Tests;

public sealed class WeeklyReportTests
{
    [Fact]
    public async Task Report_ReturnsRawArraysForTheFollowedLeaguesWithDateFilter()
    {
        var handler = new ReportHandler();
        var report = await CreateService(handler).GetAsync(StartDate, EndDate, Leagues(), CancellationToken.None);

        Assert.Equal(new[] { 6193, 6193 }, report.Divisions.Select(d => d.DivisionId));
        Assert.Equal(new[] { "Simple lettre", "Simple lettre" }, report.Divisions.Select(d => d.DivisionName));
        Assert.Equal(new[] { "U11 A League", "U11 B League" }, report.Divisions.Select(d => d.LeagueName));
        Assert.Equal(new[] { 182497, 194902 }, report.TeamIds);
        Assert.All(report.Divisions, division =>
        {
            Assert.True(division.Standings.Available);
            Assert.Equal("unchanged", division.Standings.Data!.Value[0].GetProperty("extra").GetString());
            Assert.True(division.Games.Available);
            Assert.True(division.Suspensions.Available);
        });
        // Two leagues × three sources, and no league lookup: the schedule ids
        // come from the database.
        Assert.Equal(6, handler.Requests.Count);
        var gameUris = handler.Requests.Where(uri => uri.AbsolutePath.EndsWith("/games")).ToArray();
        Assert.Equal(2, gameUris.Length);
        Assert.All(gameUris, uri =>
        {
            var filter = Uri.UnescapeDataString(uri.Query);
            Assert.Contains("2026-11-02", filter);
            Assert.Contains("2026-11-09", filter);
            Assert.Contains("scheduleId", filter);
        });
        // The Spordle calls are keyed on the stored schedule ids.
        Assert.Contains(handler.Requests, uri => uri.AbsolutePath.EndsWith("/schedules/194899/teamStats"));
        Assert.Contains(handler.Requests, uri => uri.AbsolutePath.EndsWith("/schedules/194908/teamStats"));
    }

    [Fact]
    public async Task Report_RetriesTransientErrorsAndKeepsOtherSources()
    {
        var handler = new ReportHandler(uri => uri.AbsolutePath.Contains("suspensions") ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
        var report = await CreateService(handler).GetAsync(StartDate, EndDate, Leagues(), CancellationToken.None);

        Assert.All(report.Divisions, division =>
        {
            Assert.False(division.Suspensions.Available);
            Assert.Null(division.Suspensions.Data);
            Assert.True(division.Standings.Available);
            Assert.True(division.Games.Available);
        });
        Assert.All(report.Divisions, division => Assert.Equal(
            "HTTP 503 ServiceUnavailable from Spordle", division.Suspensions.Error));
        Assert.Equal(6, handler.Requests.Count(uri => uri.AbsolutePath.Contains("suspensions")));
    }

    [Fact]
    public async Task Report_DoesNotRetryNonTransientErrors()
    {
        var handler = new ReportHandler(uri => uri.AbsolutePath.Contains("suspensions") ? HttpStatusCode.Forbidden : HttpStatusCode.OK);
        var report = await CreateService(handler).GetAsync(StartDate, EndDate, Leagues(), CancellationToken.None);
        Assert.All(report.Divisions, division =>
        {
            Assert.False(division.Suspensions.Available);
            Assert.Equal("HTTP 403 Forbidden from Spordle", division.Suspensions.Error);
        });
        Assert.Equal(2, handler.Requests.Count(uri => uri.AbsolutePath.Contains("suspensions")));
    }

    [Fact]
    public async Task Report_RetriesAndRecovers()
    {
        var attempts = 0;
        var handler = new ReportHandler(uri =>
            uri.AbsolutePath.Contains("suspensions") && Interlocked.Increment(ref attempts) <= 2
                ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
        var report = await CreateService(handler).GetAsync(StartDate, EndDate, Leagues(), CancellationToken.None);
        Assert.All(report.Divisions, division => Assert.True(division.Suspensions.Available));
        Assert.Equal(4, handler.Requests.Count(uri => uri.AbsolutePath.Contains("suspensions")));
    }

    [Fact]
    public async Task Report_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateService(new ReportHandler()).GetAsync(StartDate, EndDate, Leagues(), cancellation.Token));
    }

    [Fact]
    public async Task Report_ErrorCarriesClearMessageForClient()
    {
        var handler = new ReportHandler(uri => uri.AbsolutePath.Contains("suspensions") ? HttpStatusCode.Forbidden : HttpStatusCode.OK);
        var report = await CreateService(handler).GetAsync(StartDate, EndDate, Leagues(), CancellationToken.None);

        Assert.All(report.Divisions, division =>
        {
            Assert.False(division.Suspensions.Available);
            Assert.Null(division.Suspensions.Data);
            Assert.Equal("HTTP 403 Forbidden from Spordle", division.Suspensions.Error);
            Assert.Null(division.Standings.Error);
            Assert.Null(division.Games.Error);
        });
    }

    [Fact]
    public void Report_IgnoresNothingWhenALeagueHasNoFollowedTeam()
    {
        // A league with an empty team list is filtered out by the host, not by
        // the service; the raw record keeps whatever it was given.
        var report = new WeeklyReportResponse(
            StartDate, EndDate, [182497], [new(194899, "Simple lettre", "U11 A League", 194899, "U11 A Saison", [182497], new(true, null, null), new(true, null, null), new(true, null, null))]);
        Assert.Single(report.Divisions);
        Assert.Equal([182497], report.TeamIds);
    }

    [Fact]
    public async Task Report_KeepsTheScheduleThatPublishesEachFollowedTeam()
    {
        // A category is played in several schedules and a followed team only
        // appears in its own: a card whose standings publish none of the
        // followed teams is dropped, the other one is kept.
        var handler = new ReportHandler(standingsTeamIds: [182497]);
        var report = await CreateService(handler).GetAsync(
            StartDate, EndDate,
            [
                new(6193, "Simple lettre", 194899, "U11 A Saison", "U11 A League", [182497]),
                new(6193, "Simple lettre", 194908, "U11 A Autre", "U11 A League", [199999]),
            ],
            CancellationToken.None);

        var division = Assert.Single(report.Divisions);
        Assert.Equal(194899, division.ScheduleId);
        Assert.Equal([182497], report.TeamIds);
    }

    [Fact]
    public async Task Report_DropsAScheduleThatPublishesNoFollowedTeam()
    {
        var handler = new ReportHandler(standingsTeamIds: [999]);
        var report = await CreateService(handler).GetAsync(
            StartDate, EndDate,
            [new(6193, "Simple lettre", 194899, "U11 A Saison", "U11 A League", [182497])],
            CancellationToken.None);

        Assert.Empty(report.Divisions);
        Assert.Empty(report.TeamIds);
    }

    [Fact]
    public async Task Report_KeepsACardWhoseStandingsAreUnavailable()
    {
        // An unavailable standings table proves nothing: a transient Spordle
        // failure must not hide the league of a followed team.
        var handler = new ReportHandler(
            uri => uri.AbsolutePath.Contains("teamStats") ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
        var report = await CreateService(handler).GetAsync(StartDate, EndDate, Leagues(), CancellationToken.None);

        Assert.Equal(2, report.Divisions.Count);
        Assert.All(report.Divisions, division => Assert.False(division.Standings.Available));
    }

    private static readonly DateOnly StartDate = new(2026, 11, 2);
    private static readonly DateOnly EndDate = new(2026, 11, 9);

    /// <summary>Leagues of the report, with the schedule ids stored in the database.</summary>
    private static IReadOnlyList<WeeklyReportLeague> Leagues() =>
    [
        new(6193, "Simple lettre", 194899, "U11 A Saison", "U11 A League", [182497]),
        new(6193, "Simple lettre", 194908, "U11 B Saison", "U11 B League", [194902]),
    ];

    private static WeeklyReportService CreateService(HttpMessageHandler handler)
    {
        var options = Options.Create(new LeagueApiOptions { ApiKey = "test-key" });
        var client = new LeagueApiService(new HttpClient(handler), options, NullLogger<LeagueApiService>.Instance);
        return new WeeklyReportService(client, NullLogger<WeeklyReportService>.Instance);
    }

    private sealed class ReportHandler(Func<Uri, HttpStatusCode>? status = null, int[]? standingsTeamIds = null) : HttpMessageHandler
    {
        public ConcurrentBag<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request.RequestUri!);
            var path = request.RequestUri!.AbsolutePath;
            // The standings publish the followed team of the schedule. When the
            // test overrides the ids, the same rows are served for every
            // schedule, which is how a schedule without the team is simulated.
            var teamIds = standingsTeamIds ?? (path.Contains("/schedules/194908/") ? [194902] : [182497]);
            var standings = $"[{string.Join(",", teamIds.Select(id => $$"""{"teamId":{{id}},"extra":"unchanged"}"""))}]";
            var payload = path.EndsWith("/teamStats")
                ? standings
                : path.EndsWith("/schedules")
                    ? """[{"id":194899,"name":"U11 A League"},{"id":194901,"name":"U11 B League"}]"""
                    : """[{"extra":"unchanged"}]""";
            var response = new HttpResponseMessage(status?.Invoke(request.RequestUri!) ?? HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }
}

