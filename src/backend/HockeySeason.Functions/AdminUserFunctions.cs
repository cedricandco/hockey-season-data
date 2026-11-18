using System.Text.Json;
using HockeySeason.Application;
using HockeySeason.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;

namespace HockeySeason.Functions;

/// <summary>Request body of PUT /api/users/{userId}/roles: the complete role set.</summary>
public sealed record UserRolesRequest(List<int>? RoleIds);

/// <summary>
/// Admin: user directory and licence (role) assignment over the
/// <c>users</c>, <c>roles</c> and <c>user_roles</c> tables. Every trigger
/// requires an administrator: the Auth0 <c>roles=admin</c> claim or the
/// <c>users.is_admin</c> column (defense in depth).
/// </summary>
/// <remarks>
/// The <c>admin/</c> route prefix is reserved by the Functions host for its
/// own administration endpoints, hence the plain <c>users</c> / <c>roles</c>
/// routes here.
/// </remarks>
public sealed class AdminUserFunctions(RequestUserResolver resolver, IAdminUserService adminUsers, IConfiguration configuration)
{
    [Function("AdminListUsers")]
    public async Task<IActionResult> ListUsers(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "users")] HttpRequest request,
        FunctionContext context, CancellationToken cancellationToken)
    {
        if (await DeniedAsync(context, cancellationToken) is { } denied) return denied;

        var users = await adminUsers.GetUsersAsync(cancellationToken);
        return new OkObjectResult(new { users = users.Select(ToResponse) });
    }

    [Function("AdminListRoles")]
    public async Task<IActionResult> ListRoles(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "roles")] HttpRequest request,
        FunctionContext context, CancellationToken cancellationToken)
    {
        if (await DeniedAsync(context, cancellationToken) is { } denied) return denied;

        var roles = await adminUsers.GetRolesAsync(cancellationToken);
        return new OkObjectResult(new
        {
            roles = roles.Select(role => new { role.RoleId, role.Code, role.Name, role.MaxFollowedEntities }),
        });
    }

    [Function("AdminSetUserRoles")]
    public async Task<IActionResult> SetUserRoles(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "users/{userId:int}/roles")] HttpRequest request,
        int userId, FunctionContext context, CancellationToken cancellationToken)
    {
        if (await DeniedAsync(context, cancellationToken) is { } denied) return denied;

        var body = await ReadBodyAsync<UserRolesRequest>(request, cancellationToken);
        if (body is null || body.RoleIds is null) return InvalidBody();

        var result = await adminUsers.SetRolesAsync(userId, body.RoleIds, cancellationToken);
        return result.Error switch
        {
            AdminRoleUpdateError.None => new OkObjectResult(ToResponse(result.User!)),
            AdminRoleUpdateError.UnknownUser => new NotFoundObjectResult(new { error = result.Error.ToString() }),
            AdminRoleUpdateError.UnknownRole => new BadRequestObjectResult(new ValidationProblemDetails(
                new Dictionary<string, string[]> { ["roleIds"] = ["One or more role ids do not exist."] })),
            _ => new BadRequestResult(),
        };
    }

    /// <summary>
    /// 401 when unauthenticated, 403 when the caller is neither flagged
    /// <c>is_admin</c> in the database nor carries the <c>admin</c> claim;
    /// null for administrators. Unlike the import guard this deliberately
    /// ignores <c>Authorization:OpenAccess</c>: granting licences is a
    /// privilege operation and must never be opened to every caller.
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

    private static object ToResponse(AdminUserSummary user) => new
    {
        user.Id,
        user.Auth0UserId,
        user.Email,
        user.FirstName,
        user.LastName,
        user.IsAdmin,
        user.CreatedAt,
        Roles = user.Roles.Select(role => new { role.RoleId, role.Code, role.Name, role.MaxFollowedEntities }),
    };

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
}
