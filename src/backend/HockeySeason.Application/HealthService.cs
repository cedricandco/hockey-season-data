using HockeySeason.Domain;

namespace HockeySeason.Application;

/// <summary>
/// Single-responsibility use case: read the server time from the
/// time abstraction and return it as a <see cref="ServerTime"/>.
/// </summary>
public sealed class HealthService : IHealthService
{
    private readonly ITimeProvider _timeProvider;

    public HealthService(ITimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public ServerTime GetServerTime() => ServerTime.FromUtc(_timeProvider.UtcNow);
}
