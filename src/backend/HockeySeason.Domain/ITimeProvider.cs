namespace HockeySeason.Domain;

/// <summary>
/// Abstraction over a source of time. Consumers depend on this abstraction
/// (DIP) instead of a concrete clock, which keeps domain and application
/// code testable and free of infrastructure concerns.
/// </summary>
public interface ITimeProvider
{
    DateTimeOffset UtcNow { get; }
}
