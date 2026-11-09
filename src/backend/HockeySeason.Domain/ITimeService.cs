namespace HockeySeason.Domain;

/// <summary>
/// Domain service contract for reading the server's current time.
/// </summary>
public interface ITimeService
{
    ServerTime GetServerTime();
}
