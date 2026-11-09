using HockeySeason.Domain;

namespace HockeySeason.Application;

/// <summary>
/// Application use case: report the current server time.
/// </summary>
public interface IHealthService
{
    ServerTime GetServerTime();
}
