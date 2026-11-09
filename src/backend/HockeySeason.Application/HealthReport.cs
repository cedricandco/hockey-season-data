using HockeySeason.Domain;

namespace HockeySeason.Application;

/// <summary>
/// Payload returned by the API health endpoint. Serializes as
/// { "time": { "utcNow": "...", "timeZoneId": "UTC" }, "environment": "..." }.
/// </summary>
public sealed record HealthReport(ServerTime Time, string Environment);
