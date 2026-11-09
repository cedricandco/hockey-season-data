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
    public async Task Report_ReturnsRawArraysAndAllDivisionsWithDateFilter()
    {
        var handler = new ReportHandler();
        var report = await CreateService(handler).GetAsync(Request(), CancellationToken.None);

        Assert.Equal(new[] { 194899, 194901 }, report.Divisions.Select(d => d.DivisionId));
        Assert.Equal(new[] { "U11 A League", "U11 B League" }, report.Divisions.Select(d => d.LeagueName));
        Assert.Equal(new[] { 182497, 194901 }, report.TeamIds);
        Assert.All(report.Divisions, division =>
        {
            Assert.True(division.Standings.Available);
            Assert.Equal("unchanged", division.Standings.Data!.Value[0].GetProperty("extra").GetString());
            Assert.True(division.Games.Available);
            Assert.True(division.Suspensions.Available);
        });
        Assert.Equal(7, handler.Requests.Count);
        var gameUris = handler.Requests.Where(uri => uri.AbsolutePath.EndsWith("/games")).ToArray();
        Assert.Equal(2, gameUris.Length);
        Assert.All(gameUris, uri =>
        {
            var filter = Uri.UnescapeDataString(uri.Query);
            Assert.Contains("2026-11-02", filter);
            Assert.Contains("2026-11-09", filter);
            Assert.Contains("scheduleId", filter);
        });
    }

    [Fact]
    public async Task Report_RetriesTransientErrorsAndKeepsOtherSources()
    {
        var handler = new ReportHandler(uri => uri.AbsolutePath.Contains("suspensions") ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
        var report = await CreateService(handler).GetAsync(Request(), CancellationToken.None);

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
        var report = await CreateService(handler).GetAsync(Request(), CancellationToken.None);
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
        var report = await CreateService(handler).GetAsync(Request(), CancellationToken.None);
        Assert.All(report.Divisions, division => Assert.True(division.Suspensions.Available));
        Assert.Equal(4, handler.Requests.Count(uri => uri.AbsolutePath.Contains("suspensions")));
    }

    [Fact]
    public async Task Report_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateService(new ReportHandler()).GetAsync(Request(), cancellation.Token));
    }

    [Fact]
    public async Task Report_ErrorCarriesClearMessageForClient()
    {
        var handler = new ReportHandler(uri => uri.AbsolutePath.Contains("suspensions") ? HttpStatusCode.Forbidden : HttpStatusCode.OK);
        var report = await CreateService(handler).GetAsync(Request(), CancellationToken.None);

        Assert.All(report.Divisions, division =>
        {
            Assert.False(division.Suspensions.Available);
            Assert.Null(division.Suspensions.Data);
            Assert.Equal("HTTP 403 Forbidden from Spordle", division.Suspensions.Error);
            Assert.Null(division.Standings.Error);
            Assert.Null(division.Games.Error);
        });
    }

    [Theory]
    [InlineData("2026-11-10", "2026-11-09", "1", "2")]
    [InlineData("2026-11-01", "2026-11-09", "0", "2")]
    [InlineData("bad", "2026-11-09", "1", "2")]
    [InlineData("2026-11-01", "2026-11-09", "1", "-2")]
    public void Request_RejectsInvalidInput(string start, string end, string division, string team)
    {
        Assert.False(WeeklyReportRequest.TryParse(start, end, new StringValues(division), new StringValues(team), out _));
    }

    private static WeeklyReportRequest Request() =>
        new(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 9), [194899, 194901], [182497, 194901]);

    private static WeeklyReportService CreateService(HttpMessageHandler handler)
    {
        var options = Options.Create(new LeagueApiOptions { ApiKey = "test-key" });
        var client = new LeagueApiService(new HttpClient(handler), options, NullLogger<LeagueApiService>.Instance);
        return new WeeklyReportService(client, options, NullLogger<WeeklyReportService>.Instance);
    }

    private sealed class ReportHandler(Func<Uri, HttpStatusCode>? status = null) : HttpMessageHandler
    {
        public ConcurrentBag<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request.RequestUri!);
            var response = new HttpResponseMessage(status?.Invoke(request.RequestUri!) ?? HttpStatusCode.OK)
            {
                Content = new StringContent(
                    request.RequestUri!.AbsolutePath.EndsWith("/schedules")
                        ? """[{"id":194899,"name":"U11 A League"},{"id":194901,"name":"U11 B League"}]"""
                        : """[{"extra":"unchanged"}]""",
                    Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }
}
