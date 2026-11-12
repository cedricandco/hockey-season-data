using System.Text.Json;
using HockeySeason.Application;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace HockeySeason.Functions;

/// <summary>Request body of PUT /api/users/me/settings. Null fields are left unchanged.</summary>
public sealed record SettingsRequest(string? PrimaryColor, string? SecondaryColor, Dictionary<string, string?>? Extra);

/// <summary>
/// Local account endpoints (PostgreSQL): profile, visual settings and the
/// team / league / organization subscriptions bounded by the caller's licence.
/// Every function provisions the caller on first use through
/// <see cref="RequestUserResolver"/>.
/// </summary>
public sealed class UserFunctions(RequestUserResolver resolver, IUserService users)
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
            profile.IsAdmin,
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
        return new OkObjectResult(new
        {
            Teams = subscriptions.Teams.Select(team => new { team.EntityId, team.Name, team.CreatedAt, team.LockedUntil }),
            Leagues = subscriptions.Leagues.Select(league => new { league.EntityId, league.Name, league.CreatedAt }),
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
        return Map(result.Error); // LockedUntil is exposed via GET users/me/subscriptions
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

        var body = await ReadBodyAsync<CreateSubscriptionRequest>(request, cancellationToken);
        if (body is null) return InvalidBody();

        var userId = await resolver.GetOrCreateUserIdAsync(context, cancellationToken);
        var result = await users.AddLeagueSubscriptionAsync(userId, body.EntityId, cancellationToken);
        return Map(result.Error);
    }

    [Function("RemoveLeagueSubscription")]
    public async Task<IActionResult> RemoveLeague(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "users/me/league-subscriptions/{leagueId:int}")] HttpRequest request,
        int leagueId, FunctionContext context, CancellationToken cancellationToken)
    {
        if (Unauthorized(context) is { } denied) return denied;

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
        _ => new BadRequestResult(),
    };
}
