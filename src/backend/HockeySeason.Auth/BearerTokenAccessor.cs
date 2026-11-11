using Microsoft.AspNetCore.Http;

namespace HockeySeason.Auth;

/// <summary>
/// Extracts the bearer token of the current HTTP request. Reads the standard
/// <c>Authorization</c> header first and falls back to <c>X-Authorization</c>,
/// which the SPA mirrors on every call for hosts/proxies that drop the
/// standard header.
/// </summary>
public interface IBearerTokenAccessor
{
    /// <summary>Returns the raw bearer token of the current request, without the scheme.</summary>
    string? GetBearerToken();
}

/// <summary>Parses the "Bearer &lt;token&gt;" value out of a raw header string.</summary>
public static class BearerTokenParser
{
    /// <summary>Extracts the token from a raw "Bearer ..." header value.</summary>
    public static bool TryExtractBearerToken(string? headerValue, out string token)
    {
        token = string.Empty;
        if (string.IsNullOrWhiteSpace(headerValue))
        {
            return false;
        }

        const string Scheme = "Bearer ";
        if (!headerValue.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        token = headerValue[Scheme.Length..].Trim();
        return token.Length > 0;
    }
}

/// <inheritdoc />
public sealed class BearerTokenAccessor(IHttpContextAccessor httpContextAccessor) : IBearerTokenAccessor
{
    /// <inheritdoc />
    public string? GetBearerToken()
    {
        var headers = httpContextAccessor.HttpContext?.Request.Headers;
        if (headers is null)
        {
            return null;
        }

        if (BearerTokenParser.TryExtractBearerToken(headers.Authorization.ToString(), out var token))
        {
            return token;
        }

        return headers.TryGetValue(UserContextExtensions.MirroredAuthorizationHeader, out var mirrored)
            && BearerTokenParser.TryExtractBearerToken(mirrored.ToString(), out token)
            ? token
            : null;
    }
}
