using HockeySeason.Auth;
using Microsoft.AspNetCore.Http;

namespace HockeySeason.Agents;

/// <summary>
/// Provides agent code with the current request's mirrored bearer token.
/// The token stays in the HTTP request and is never added to model messages.
/// </summary>
public sealed class AgentCallerContext(IHttpContextAccessor httpContextAccessor)
{
    public string? GetAccessToken()
    {
        var headers = httpContextAccessor.HttpContext?.Request.Headers;
        return headers is not null
            && headers.TryGetValue(UserContextExtensions.MirroredAuthorizationHeader, out var value)
            && BearerTokenParser.TryExtractBearerToken(value.ToString(), out var token)
                ? token
                : null;
    }
}
