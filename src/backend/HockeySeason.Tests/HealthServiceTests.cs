using HockeySeason.Application;
using HockeySeason.Domain;
using Xunit;

namespace HockeySeason.Tests;

/// <summary>
/// Deterministic time provider for tests.
/// </summary>
internal sealed class FixedTimeProvider(DateTimeOffset utcNow) : ITimeProvider
{
    public DateTimeOffset UtcNow => utcNow;
}

public sealed class HealthServiceTests
{
    private static readonly DateTimeOffset Fixed = new(2026, 11, 9, 2, 45, 0, TimeSpan.Zero);

    [Fact]
    public void GetServerTime_ReturnsTimeFromProvider()
    {
        IHealthService sut = new HealthService(new FixedTimeProvider(Fixed));

        var result = sut.GetServerTime();

        Assert.Equal(Fixed, result.UtcNow);
    }

    [Fact]
    public void GetServerTime_ReportsUtcTimeZone()
    {
        IHealthService sut = new HealthService(new FixedTimeProvider(Fixed));

        var result = sut.GetServerTime();

        Assert.Equal("UTC", result.TimeZoneId);
    }
}
