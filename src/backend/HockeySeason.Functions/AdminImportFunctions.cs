using System.Globalization;
using HockeySeason.Application;
using HockeySeason.LeagueApi;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HockeySeason.Functions;

/// <summary>
/// Admin: historical Spordle import triggers. The association/season import of
/// <see cref="AdminReferenceDataFunctions"/> (POST /api/reference/import) is the
/// supported entry point; the <c>import/*</c> routes are kept as thin aliases
/// so existing scripts keep working, and both delegate to
/// <see cref="IReferenceDataService"/> — the Spordle call and the upsert logic
/// live in exactly one place.
/// </summary>
public sealed class AdminImportFunctions(
    IReferenceDataService referenceData,
    IOptions<LeagueApiOptions> options,
    IConfiguration configuration,
    ILogger<AdminImportFunctions> logger)
{
    /// <summary>
    /// Imports every configured office for a season (query <c>seasonId</c>,
    /// default from configuration). Superseded by
    /// <c>POST /api/reference/import</c>, which does the same for one office.
    /// </summary>
    [Function("ImportOffices")]
    public async Task<IActionResult> ImportOffices(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "import/offices")] HttpRequest request,
        FunctionContext context, CancellationToken cancellationToken)
    {
        if (Denied(context, configuration) is { } denied) return denied;

        var seasonId = request.Query["seasonId"].ToString();
        var season = string.IsNullOrWhiteSpace(seasonId) ? options.Value.DefaultSeasonId : seasonId;

        var imported = 0;
        foreach (var office in LeagueOffices.All)
        {
            try
            {
                // The alias keeps its historical behaviour: the office doubles
                // as the association, created on the first run.
                var organization = await referenceData.EnsureOrganizationAsync(
                    office.Id.ToString(CultureInfo.InvariantCulture), office.Name, cancellationToken);
                var result = await referenceData.ImportAsync(
                    new ReferenceImportRequest(
                        organization.Id, office.Id.ToString(CultureInfo.InvariantCulture), season),
                    cancellationToken);
                if (result.CreatedLeagues.Count > 0 || result.CreatedTeams > 0)
                {
                    imported++;
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
            {
                // One unavailable office keeps the run partial, as before.
                logger.LogWarning(exception, "Office {OfficeId} could not be imported.", office.Id);
            }
        }

        return new OkObjectResult(new { imported, seasonId = season });
    }

    /// <summary>
    /// Leagues and teams are imported together (a league now groups teams):
    /// this alias points the caller at the supported endpoint instead of
    /// duplicating the import.
    /// </summary>
    [Function("ImportLeagues")]
    public IActionResult ImportLeagues(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "import/leagues")] HttpRequest request,
        FunctionContext context)
    {
        if (Denied(context, configuration) is { } denied) return denied;

        return Superseded();
    }

    /// <summary>See <see cref="ImportLeagues"/>.</summary>
    [Function("ImportTeams")]
    public IActionResult ImportTeams(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "import/teams")] HttpRequest request,
        FunctionContext context)
    {
        if (Denied(context, configuration) is { } denied) return denied;

        return Superseded();
    }

    private static BadRequestObjectResult Superseded() => new(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = "use_reference_import",
        Detail = "Leagues and teams are imported together by POST /api/reference/import " +
            "({ organizationExternalId, officeId, seasonId }).",
    });

    /// <summary>401 when unauthenticated, 403 when the caller is not an admin and open access is off; null otherwise.</summary>
    private static IActionResult? Denied(FunctionContext context, IConfiguration configuration)
    {
        var authorization = FunctionsAuthorization.IsAuthenticatedV2(context);
        if (!authorization.IsAuthenticated)
        {
            return FunctionsAuthorization.UnauthorizedResult(authorization.Reason);
        }
        if (configuration.GetSection(AuthorizationOptions.SectionName).Get<AuthorizationOptions>() is { OpenAccess: true })
        {
            return null;
        }
        return RequestUserResolver.IsAdmin(context) ? null : new StatusCodeResult(StatusCodes.Status403Forbidden);
    }
}
