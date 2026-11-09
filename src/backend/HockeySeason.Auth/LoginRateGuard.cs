using System.Collections.Concurrent;
using System.Threading.RateLimiting;

namespace HockeySeason.Auth;

/// <summary>
/// Fixed-window brute-force guard for the login endpoint: at most
/// <see cref="AttemptLimit"/> attempts per <see cref="Window"/> per client IP.
/// Implemented here (instead of the ASP.NET rate-limiter middleware) so the
/// exact same behavior applies to the minimal-API host and Azure Functions.
/// A per-IP window is only created when that IP first attempts a login.
/// </summary>
public sealed class LoginRateGuard : IDisposable
{
    public const int AttemptLimit = 5;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, FixedWindowRateLimiter> _limiters = new();

    /// <summary>Attempts to reserve one attempt for the given client key.</summary>
    public bool TryAcquire(string clientKey)
    {
        var limiter = _limiters.GetOrAdd(clientKey, CreateLimiter);
        using var lease = limiter.AttemptAcquire();
        return lease.IsAcquired;
    }

    /// <summary>Seconds the client should wait before retrying (for Retry-After).</summary>
    public int RetryAfterSeconds(string clientKey)
    {
        var window = Window.TotalSeconds;
        return (int)Math.Ceiling(window);
    }

    private static FixedWindowRateLimiter CreateLimiter(string clientKey) =>
        new(new FixedWindowRateLimiterOptions
        {
            PermitLimit = AttemptLimit,
            Window = Window,
            QueueLimit = 0,
        });

    public void Dispose()
    {
        foreach (var limiter in _limiters.Values)
        {
            limiter.Dispose();
        }
        _limiters.Clear();
    }
}
