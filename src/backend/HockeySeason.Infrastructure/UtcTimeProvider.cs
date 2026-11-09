namespace HockeySeason.Infrastructure;

/// <summary>
/// Adapter over the system clock that satisfies the domain's
/// <c>ITimeProvider</c> abstraction (Dependency Inversion).
/// </summary>
public sealed class UtcTimeProvider : HockeySeason.Domain.ITimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
