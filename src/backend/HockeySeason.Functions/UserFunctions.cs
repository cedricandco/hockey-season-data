using System.Text.Json;
using HockeySeason.Application;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace HockeySeason.Functions;

/// <summary>Request body of POST /api/users/me/settings. Null fields are left unchanged.</summary>
public sealed record SettingsRequest(string? PrimaryColor, string? SecondaryColor, Dictionary<string, string?>? Extra);

/// <summary>
/// Request body of POST /api/users/me/league-subscriptions: the Spordle
/// category id of the league, a guid string.
/// </summary>
public sealed record CreateLeagueSubscriptionRequest(string? LeagueId);

/// <summary>
/// Local account endpoints (PostgreSQL): profile, visual settings and the
/// team / league / organization subscriptions bounded by the caller's licence.
/// Every function provisions the caller on first use through
/// <see cref="RequestUserResolver"/>.
/// </summary>
public sealed class UserFunctions(
    RequestUserResolver resolver,
    IUserService users,
    ISubscriptionCatalogService catalog)
{
    [Function("UserAccount")]
    public async Task<IActionResult> Account(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "users/me")] HttpRequest request,
        FunctionContext context, CancellationToken cancellationToken)
    {
        if (Unauthorized(context) is { } denied) return denied;

        var userId = await resolver.GetOrCreateUserIdAsync(context, cancellationToken);
        var profile = await users.GetProfileAsync(userId, cancellationToken);
        return profile is null ? new NotFoundResult() : new OkObjectResult(new
        {
            profile.Id,
            profile.Auth0UserId,
            profile.Email,
            profile.FirstName,
            profile.LastName,
            // Defense in depth: the database flag or the Auth0 admin claim
            // unlocks the admin UI, matching the guards of the admin triggers.
            IsAdmin = profile.IsAdmin || RequestUserResolver.IsAdmin(context),
            Roles = profile.Roles.Select(role => new { role.RoleId, Code = role.Code.ToString(), role.DisplayName, role.MaxFollowedEntities }),
            Settings = new { profile.PrimaryColor, profile.SecondaryColor, profile.Extra },
        });
    }

    [Function("UpdateUserSettings")]
    public async Task<IActionResult> UpdateSettings(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "users/me/settings")] HttpRequest request,
        FunctionContext context, CancellationToken cancellationToken)
    {
        if (Unauthorized(context) is { } denied) return denied;

        var body = await ReadBodyAsync<SettingsRequest>(request, cancellationToken);
        if (body is null) return InvalidBody();

        var userId = await resolver.GetOrCreateUserIdAsync(context, cancellationToken);
        await users.UpdateSettingsAsync(userId, new(body.PrimaryColor, body.SecondaryColor, body.Extra), cancellationToken);
        return new OkObjectResult(new { status = "updated" });
    }

    [Function("UserSubscriptions")]
    public async Task<IActionResult> Subscriptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "users/me/subscriptions")] HttpRequest request,
        FunctionContext context, CancellationToken cancellationToken)
    {
        if (Unauthorized(context) is { } denied) return denied;

        var userId = await resolver.GetOrCreateUserIdAsync(context, cancellationToken);
        var subscriptions = await users.GetSubscriptionsAsync(userId, cancellationToken);
        var limits = await users.GetLimitsAsync(userId, cancellationToken);
        return new OkObjectResult(new
        {
            Teams = subscriptions.Teams.Select(team => new { team.TeamId, team.Name, team.CreatedAt, team.LockedUntil }),
            Leagues = subscriptions.Leagues.Select(league => new { league.LeagueId, league.Name, league.CreatedAt }),
            Organizations = subscriptions.Organizations.Select(organization =>
                new { organization.OrganizationId, organization.Name, organization.CreatedAt }),
            Limits = limits.Select(limit => new { limit.Kind, limit.Max, limit.Used, limit.CanAddMore }),
        });
    }

    /// <summary>
    /// Associations offered by the "follow a team" selector. Any authenticated
    /// caller may read the reference data: it is shared and read-only.
    /// </summary>
    [Function("SubscriptionAssociations")]
    public async Task<IActionResult> Associations(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "subscriptions/associations")] HttpRequest request,
        FunctionContext context, CancellationToken cancellationToken)
    {
        if (Unauthorized(context) is { } denied) return denied;

        var associations = await catalog.GetAssociationsAsync(cancellationToken);
        return new OkObjectResult(new
        {
            associations = associations.Select(item => new { item.Id, item.Name }),
        });
    }

    /// <summary>Divisions (Spordle offices) of one association.</summary>
    [Function("SubscriptionDivisions")]
    public async Task<IActionResult> Divisions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "subscriptions/associations/{associationId:int}/divisions")] HttpRequest request,
        int associationId, FunctionContext context, CancellationToken cancellationToken)
    {
        if (Unauthorized(context) is { } denied) return denied;
        if (associationId <= 0) return InvalidBody();

        var divisions = await catalog.GetDivisionsAsync(associationId, cancellationToken);
        return new OkObjectResult(new
        {
            divisions = divisions.Select(item => new { item.Id, item.Name }),
        });
    }

    /// <summary>
    /// Leagues (Spordle categories, French name) of a division for a season;
    /// the latest imported season is used when <c>seasonId</c> is omitted.
    /// </summary>
    [Function("SubscriptionLeagues")]
    public async Task<IActionResult> LeagueCatalog(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "subscriptions/divisions/{divisionId:int}/leagues")] HttpRequest request,
        int divisionId, FunctionContext context, CancellationToken cancellationToken)
    {
        if (Unauthorized(context) is { } denied) return denied;
        if (divisionId <= 0) return InvalidBody();

        var season = request.Query["seasonId"].ToString();
        var options = await catalog.GetLeaguesAsync(
            divisionId, string.IsNullOrWhiteSpace(season) ? null : season.Trim(), cancellationToken);
        return new OkObjectResult(new
        {
            options.SeasonId,
            leagues = options.Leagues.Select(league => new { league.Id, league.Name }),
        });
    }

    /// <summary>Teams of one league (Spordle category guid), by Spordle team id.</summary>
    [Function("SubscriptionTeams")]
    public async Task<IActionResult> TeamCatalog(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "subscriptions/leagues/{leagueId}/teams")] HttpRequest request,
        string leagueId, FunctionContext context, CancellationToken cancellationToken)
    {
        if (Unauthorized(context) is { } denied) return denied;
        if (string.IsNullOrWhiteSpace(leagueId)) return InvalidBody();

        var teams = await catalog.GetTeamsAsync(leagueId, cancellationToken);
        return new OkObjectResult(new
        {
            teams = teams.Select(item => new { item.Id, item.Name }),
        });
    }

    [Function("AddTeamSubscription")]
    public async Task<IActionResult> AddTeam(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "users/me/team-subscriptions")] HttpRequest request,
        FunctionContext context, CancellationToken cancellationToken)
    {
        if (Unauthorized(context) is { } denied) return denied;

        var body = await ReadBodyAsync<CreateSubscriptionRequest>(request, cancellationToken);
        if (body is null) return InvalidBody();

        var userId = await resolver.GetOrCreateUserIdAsync(context, cancellationToken);
        var result = await users.AddTeamSubscriptionAsync(userId, body.EntityId, cancellationToken);
        return Map(result.Error); // LockedUntil and headroom come from GET users/me/subscriptions
    }

    [Function("RemoveTeamSubscription")]
    public async Task<IActionResult> RemoveTeam(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "users/me/team-subscriptions/{teamId:int}")] HttpRequest request,
        int teamId, FunctionContext context, CancellationToken cancellationToken)
    {
        if (Unauthorized(context) is { } denied) return denied;

        var userId = await resolver.GetOrCreateUserIdAsync(context, cancellationToken);
        var error = await users.RemoveTeamSubscriptionAsync(userId, teamId, RequestUserResolver.IsAdmin(context), cancellationToken);
        return Map(error);
    }

    [Function("RequestTeamUnlock")]
    public async Task<IActionResult> RequestTeamUnlock(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "users/me/team-subscriptions/{teamId:int}/unlock-request")] HttpRequest request,
        int teamId, FunctionContext context, CancellationToken cancellationToken)
    {
        if (Unauthorized(context) is { } denied) return denied;

        var userId = await resolver.GetOrCreateUserIdAsync(context, cancellationToken);
        await users.RequestTeamUnlockAsync(userId, teamId, cancellationToken);
        return new AcceptedResult();
    }

    [Function("AddLeagueSubscription")]
    public async Task<IActionResult> AddLeague(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "users/me/league-subscriptions")] HttpRequest request,
        FunctionContext context, CancellationToken cancellationToken)
    {
        if (Unauthorized(context) is { } denied) return denied;

        var body = await ReadBodyAsync<CreateLeagueSubscriptionRequest>(request, cancellationToken);
        if (body is null || string.IsNullOrWhiteSpace(body.LeagueId)) return InvalidBody();

        var userId = await resolver.GetOrCreateUserIdAsync(context, cancellationToken);
        var result = await users.AddLeagueSubscriptionAsync(userId, body.LeagueId, cancellationToken);
        return Map(result.Error);
    }

    [Function("RemoveLeagueSubscription")]
    public async Task<IActionResult> RemoveLeague(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "users/me/league-subscriptions/{leagueId}")] HttpRequest request,
        string leagueId, FunctionContext context, CancellationToken cancellationToken)
    {
        if (Unauthorized(context) is { } denied) return denied;
        if (string.IsNullOrWhiteSpace(leagueId)) return InvalidBody();

        var userId = await resolver.GetOrCreateUserIdAsync(context, cancellationToken);
        return Map(await users.RemoveLeagueSubscriptionAsync(userId, leagueId, cancellationToken));
    }

    [Function("AddOrganizationSubscription")]
    public async Task<IActionResult> AddOrganization(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "users/me/organization-subscriptions")] HttpRequest request,
        FunctionContext context, CancellationToken cancellationToken)
    {
        if (Unauthorized(context) is { } denied) return denied;

        var body = await ReadBodyAsync<CreateSubscriptionRequest>(request, cancellationToken);
        if (body is null) return InvalidBody();

        var userId = await resolver.GetOrCreateUserIdAsync(context, cancellationToken);
        var result = await users.AddOrganizationSubscriptionAsync(userId, body.EntityId, cancellationToken);
        return Map(result.Error);
    }

    [Function("RemoveOrganizationSubscription")]
    public async Task<IActionResult> RemoveOrganization(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "users/me/organization-subscriptions/{organizationId:int}")] HttpRequest request,
        int organizationId, FunctionContext context, CancellationToken cancellationToken)
    {
        if (Unauthorized(context) is { } denied) return denied;

        var userId = await resolver.GetOrCreateUserIdAsync(context, cancellationToken);
        return Map(await users.RemoveOrganizationSubscriptionAsync(userId, organizationId, cancellationToken));
    }

    private static ObjectResult? Unauthorized(FunctionContext context)
    {
        var authorization = FunctionsAuthorization.IsAuthenticatedV2(context);
        return authorization.IsAuthenticated ? null : FunctionsAuthorization.UnauthorizedResult(authorization.Reason);
    }

    private static readonly JsonSerializerOptions BodyOptions = new(JsonSerializerDefaults.Web);

    private static async Task<T?> ReadBodyAsync<T>(HttpRequest request, CancellationToken cancellationToken) where T : class
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(request.Body, BodyOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static BadRequestObjectResult InvalidBody() => new(new ValidationProblemDetails(
        new Dictionary<string, string[]> { ["body"] = ["A valid JSON request body is required."] }));

    private static IActionResult Map(SubscriptionError error) => error switch
    {
        SubscriptionError.None => new NoContentResult(),
        SubscriptionError.UnknownEntity => new NotFoundObjectResult(new { error = error.ToString() }),
        SubscriptionError.LimitReached => new ObjectResult(new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "licence_limit_reached",
            Detail = "The licence of this role does not allow following more entities of this kind.",
        })
        {
            StatusCode = StatusCodes.Status409Conflict,
        },
        SubscriptionError.OutOfScope => new StatusCodeResult(StatusCodes.Status403Forbidden),
        SubscriptionError.Duplicate => new ConflictObjectResult(new { error = "already_followed" }),
        SubscriptionError.Locked => new ObjectResult(new ProblemDetails
        {
            Status = StatusCodes.Status423Locked,
            Title = "team_locked",
            Detail = "The team was followed less than 15 days ago: removal is locked until the lock window ends.",
        })
        {
            StatusCode = StatusCodes.Status423Locked,
        },
        _ => new BadRequestResult(),
    };
}
