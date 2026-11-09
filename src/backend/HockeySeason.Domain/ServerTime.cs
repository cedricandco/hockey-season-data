namespace HockeySeason.Domain;

/// <summary>
/// Snapshot of the server's clock, always expressed in UTC.
/// </summary>
public sealed record ServerTime(DateTimeOffset UtcNow, string TimeZoneId)
{
    /// <summary>
    /// Creates a <see cref="ServerTime"/> from a UTC timestamp.
    /// </summary>
    public static ServerTime FromUtc(DateTimeOffset utcNow) => new(utcNow, "UTC");
}
