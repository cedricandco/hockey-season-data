namespace HockeySeason.Domain;

/// <summary>
/// Domain service that turns a raw UTC timestamp from the time provider
/// into a <see cref="ServerTime"/> value.
/// </summary>
public sealed class TimeService : ITimeService
{
    private readonly ITimeProvider _timeProvider;

    public TimeService(ITimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public ServerTime GetServerTime() => ServerTime.FromUtc(_timeProvider.UtcNow);
}
