using System.Security.Claims;
using HockeySeason.Application;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace HockeySeason.Mcp;

/// <summary>
/// Identity of the caller of the current tool invocation: the JWT subject of
/// the request (standard or mirrored bearer token, whichever the host
/// validated) and the open-access switch.
/// </summary>
/// <remarks>
/// Tools are resolved once per host, so the identity is read from the request
/// at call time rather than captured at construction — the same pattern as the
/// agent's <c>AgentCallerContext</c>.
/// </remarks>
public sealed class CallerIdentity(IHttpContextAccessor httpContextAccessor, IOptions<AuthorizationOptions> authorization)
{
    /// <summary>Whether licence and scope restrictions are lifted (open access).</summary>
    public bool Unrestricted => authorization.Value.OpenAccess;

    /// <summary>Auth0 subject (<c>sub</c>) of the authenticated caller, or null.</summary>
    public string? Subject
    {
        get
        {
            var principal = httpContextAccessor.HttpContext?.User;
            return principal?.Identity?.IsAuthenticated == true
                ? principal.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? principal.FindFirstValue("sub")
                : null;
        }
    }
}
