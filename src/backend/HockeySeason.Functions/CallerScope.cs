using HockeySeason.Application;
using HockeySeason.Infrastructure.Data;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;

namespace HockeySeason.Functions;

/// <summary>
/// Scope enforcement for business functions: the caller-supplied Spordle ids
/// are validated against the user's authorized scope (team/league/organization
/// subscriptions). Query parameters are never trusted. While
/// <c>Authorization:OpenAccess</c> is on — or when no database is configured —
/// there is no scope to enforce and functions run unfiltered.
/// </summary>
public sealed class CallerScope(RequestUserResolver resolver, IScopeService scopes, IConfiguration configuration)
{
    /// <summary>The caller's scope, or null when unrestricted.</summary>
    public async Task<UserScope?> GetAsync(FunctionContext context, CancellationToken cancellationToken)
    {
        if (configuration.GetSection(AuthorizationOptions.SectionName).Get<AuthorizationOptions>() is { OpenAccess: true })
        {
            return null;
        }

        if (!DatabaseOptions.IsPersistenceConfigured(configuration))
        {
            return null;
        }

        var userId = await resolver.GetOrCreateUserIdAsync(context, cancellationToken);
        return await scopes.GetScopeAsync(userId, cancellationToken);
    }
}
