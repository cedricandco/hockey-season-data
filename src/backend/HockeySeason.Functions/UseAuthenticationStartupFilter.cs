using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace HockeySeason.Functions;

/// <summary>
/// Injects <c>UseAuthentication()</c> into the ASP.NET Core pipeline before
/// the Functions middleware runs. <c>ConfigureFunctionsWebApplication()</c>
/// builds an ASP.NET Core pipeline but does not wire authentication up, so
/// without this filter <c>HttpContext.User</c> is always unauthenticated in
/// isolated-process mode. <see cref="IStartupFilter"/> runs before the host's
/// own <c>Configure</c>, keeping <c>UseAuthentication</c> ahead of the
/// routing/endpoints middleware regardless of hosting platform.
/// </summary>
internal sealed class UseAuthenticationStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
        app =>
        {
            app.UseAuthentication();
            app.UseAuthorization();
            next(app);
        };
}
