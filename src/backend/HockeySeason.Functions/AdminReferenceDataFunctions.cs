using System.Globalization;
using System.Text.Json;
using HockeySeason.Application;
using HockeySeason.Infrastructure.Data;
using HockeySeason.LeagueApi;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace HockeySeason.Functions;

/// <summary>Request body of POST /api/reference/import.</summary>
/// <param name="OrganizationId">Local id of the association (a `organizations` row).</param>
/// <param name="DivisionId">Division number typed in the page (a `divisions.external_id`).</param>
/// <param name="SeasonId">Spordle season id, e.g. "2026-27".</param>
public sealed record ReferenceImportBody(int? OrganizationId, string? DivisionId, string? SeasonId);

/// <summary>
/// Admin: reference-data import of an association from the public Spordle
/// league API. One call imports a whole office and season: the division
/// (office), the season, every league (category, named in French) and every
/// team, in an idempotent upsert that also deletes what disappeared from
/// Spordle. Requires an administrator, like the user-directory triggers.
/// </summary>
/// <remarks>
/// The <c>admin/</c> route prefix is reserved by the Functions host for its
/// own administration endpoints, hence the <c>reference/</c> prefix. The
/// Spordle call and the reconciliation live in
/// <see cref="IReferenceDataService"/>; the historical <c>import/*</c> triggers
/// delegate to it as well, so there is a single implementation.
/// </remarks>
public sealed class AdminReferenceDataFunctions(
    IReferenceDataService referenceData,
    RequestUserResolver resolver,
    IAdminUserService adminUsers,
    IOptions<LeagueApiOptions> options,
    IConfiguration configuration)
{
    private static readonly JsonSerializerOptions BodyOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Associations that can be imported (organizations with a Spordle mirror).</summary>
    [Function("ReferenceOrganizations")]
    public async Task<IActionResult> Organizations(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "reference/organizations")] HttpRequest request,
        FunctionContext context, CancellationToken cancellationToken)
    {
        if (await DeniedAsync(context, cancellationToken) is { } denied) return denied;

        var organizations = await referenceData.GetOrganizationsAsync(cancellationToken);
        return new OkObjectResult(new
        {
            organizations = organizations.Select(organization => new
            {
                organization.Id,
                organization.Name,
                organization.ExternalId,
            }),
        });
    }

    /// <summary>Seasons offered for a division (its Spordle office number).</summary>
    [Function("ReferenceSeasons")]
    public async Task<IActionResult> Seasons(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "reference/divisions/{divisionId:int}/seasons")] HttpRequest request,
        int divisionId, FunctionContext context, CancellationToken cancellationToken)
    {
        if (await DeniedAsync(context, cancellationToken) is { } denied) return denied;
        if (divisionId <= 0) return InvalidBody();

        var seasons = await referenceData.GetSeasonsAsync(divisionId, cancellationToken);
        return new OkObjectResult(new
        {
            seasons = seasons.Select(season => new { season.ExternalId, season.Name, season.IsCurrent }),
            defaultSeasonId = seasons.FirstOrDefault(season => season.IsCurrent)?.ExternalId
                ?? options.Value.DefaultSeasonId,
        });
    }

    /// <summary>Imports the leagues and teams of a division for a season.</summary>
    [Function("ReferenceImport")]
    public async Task<IActionResult> Import(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "reference/import")] HttpRequest request,
        FunctionContext context, CancellationToken cancellationToken)
    {
        if (await DeniedAsync(context, cancellationToken) is { } denied) return denied;

        ReferenceImportBody? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<ReferenceImportBody>(request.Body, BodyOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return InvalidBody();
        }

        if (body is null || body.OrganizationId is not > 0 || string.IsNullOrWhiteSpace(body.DivisionId))
        {
            return InvalidBody();
        }

        var divisionId = body.DivisionId.Trim();
        var seasonId = string.IsNullOrWhiteSpace(body.SeasonId) ? options.Value.DefaultSeasonId : body.SeasonId.Trim();

        ReferenceImportResult result;
        try
        {
            result = await referenceData.ImportAsync(
                new ReferenceImportRequest(body.OrganizationId.Value, divisionId, seasonId), cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            // Unknown association or division number: the caller has to fix the input.
            return new BadRequestObjectResult(new ValidationProblemDetails(
                new Dictionary<string, string[]> { ["divisionId"] = [exception.Message] }));
        }
        catch (HttpRequestException exception)
        {
            return new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status502BadGateway,
                Title = "spordle_unavailable",
                Detail = exception.Message,
            })
            {
                StatusCode = StatusCodes.Status502BadGateway,
            };
        }

        return new OkObjectResult(new
        {
            result.OrganizationName,
            result.OfficeName,
            result.SeasonId,
            Leagues = new
            {
                Created = result.CreatedLeagues,
                Updated = result.UpdatedLeagues,
                Deleted = result.DeletedLeagues,
                Unchanged = result.UnchangedLeagues,
            },
            Teams = new
            {
                Created = result.CreatedTeams,
                Updated = result.UpdatedTeams,
                Deleted = result.DeletedTeams,
            },
            result.Warnings,
        });
    }

    /// <summary>
    /// 401 when unauthenticated, 403 when the caller is neither flagged
    /// <c>is_admin</c> in the database nor carries the <c>admin</c> claim;
    /// null for administrators. Reference data is shared by every user, so the
    /// import stays closed even while <c>Authorization:OpenAccess</c> is on
    /// (unlike the legacy <c>import/*</c> triggers).
    /// </summary>
    private async Task<IActionResult?> DeniedAsync(FunctionContext context, CancellationToken cancellationToken)
    {
        var authorization = FunctionsAuthorization.IsAuthenticatedV2(context);
        if (!authorization.IsAuthenticated)
        {
            return FunctionsAuthorization.UnauthorizedResult(authorization.Reason);
        }

        if (RequestUserResolver.IsAdmin(context))
        {
            return null;
        }

        // Without a database the flag cannot be read: deny instead of failing.
        if (!DatabaseOptions.IsPersistenceConfigured(configuration))
        {
            return new StatusCodeResult(StatusCodes.Status403Forbidden);
        }

        var userId = await resolver.GetOrCreateUserIdAsync(context, cancellationToken);
        return await adminUsers.IsUserAdminAsync(userId, cancellationToken)
            ? null
            : new StatusCodeResult(StatusCodes.Status403Forbidden);
    }

    private static BadRequestObjectResult InvalidBody() => new(new ValidationProblemDetails(
        new Dictionary<string, string[]> { ["body"] = ["A valid JSON request body is required."] }));
}
