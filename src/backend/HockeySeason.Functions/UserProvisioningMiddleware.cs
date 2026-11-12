using HockeySeason.Infrastructure.Data;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HockeySeason.Functions;

/// <summary>
/// Registers the caller's local account on every authenticated invocation
/// (idempotent): the users row is provisioned the first time someone connects,
/// whatever function the client calls first.
///
/// Skipped when persistence is not configured (no database, nothing to
/// register). Never rejects or blocks the invocation: failures are logged,
/// anonymous probes stay anonymous and each trigger keeps enforcing its own
/// 401 through <see cref="FunctionsAuthorization"/>.
/// </summary>
public sealed class UserProvisioningMiddleware(IConfiguration configuration, ILogger<UserProvisioningMiddleware> logger)
    : IFunctionsWorkerMiddleware
{
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        if (DatabaseOptions.IsPersistenceConfigured(configuration) && context.GetHttpContext() is not null)
        {
            try
            {
                if (FunctionsAuthorization.IsAuthenticatedV2(context).IsAuthenticated)
                {
                    var resolver = context.InstanceServices.GetRequiredService<RequestUserResolver>();
                    await resolver.GetOrCreateUserIdAsync(context, context.CancellationToken);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception,
                    "User provisioning failed on an authenticated invocation; the invocation continues and database-bound functions will surface their own error.");
            }
        }

        await next(context);
    }
}
