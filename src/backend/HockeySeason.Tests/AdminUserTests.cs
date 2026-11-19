using System.Security.Claims;
using System.Text;
using System.Text.Json;
using HockeySeason.Application;
using HockeySeason.Functions;
using HockeySeason.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HockeySeason.Tests;

/// <summary>
/// Admin user directory and licence assignment: the persistence use case over
/// the users/roles/user_roles tables and the administrator guard of the
/// HTTP triggers.
/// </summary>
public sealed class AdminUserTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<HockeySeasonDbContext> _options;
    private readonly FixedTimeProvider _time = new(new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));

    public AdminUserTests() =>
        _options = new DbContextOptionsBuilder<HockeySeasonDbContext>().UseSqlite(_connection).Options;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        await using var db = new HockeySeasonDbContext(_options);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    // ─── Persistence use case ──────────────────────────────────────────────

    [Fact]
    public async Task SetRoles_ReplacesTheWholeRoleSetOfTheUser()
    {
        var users = new AdminUserService(new TestDbContextFactory(_options), _time);
        var userId = await SeedUserAsync("auth0|admin-edited", isAdmin: false);

        var granted = await users.SetRolesAsync(userId, [1, 3]);
        Assert.Equal(AdminRoleUpdateError.None, granted.Error);
        Assert.Equal([1, 3], granted.User!.Roles.Select(role => role.RoleId));

        // Second call revokes president and keeps coach: the COACH licence is
        // the default of the software and cannot be taken away.
        var narrowed = await users.SetRolesAsync(userId, [3]);
        Assert.Equal(AdminRoleUpdateError.None, narrowed.Error);
        Assert.Equal([1, 3], narrowed.User!.Roles.Select(role => role.RoleId));

        // Even an empty request leaves the default COACH licence in place.
        var cleared = await users.SetRolesAsync(userId, []);
        Assert.Equal([1], cleared.User!.Roles.Select(role => role.RoleId));

        // Idempotent: no duplicate rows are appended.
        await users.SetRolesAsync(userId, [1, 3, 3]);
        await using var db = new HockeySeasonDbContext(_options);
        Assert.Equal(2, await db.UserRoles.CountAsync(userRole => userRole.UserId == userId));

        var listed = Assert.Single(await users.GetUsersAsync());
        Assert.Equal(userId, listed.Id);
        Assert.Equal([1, 3], listed.Roles.Select(role => role.RoleId));
    }

    [Fact]
    public async Task SetRoles_RejectsUnknownRoleAndUnknownUser()
    {
        var users = new AdminUserService(new TestDbContextFactory(_options), _time);
        var userId = await SeedUserAsync("auth0|no-such-role", isAdmin: false);

        Assert.Equal(AdminRoleUpdateError.UnknownRole, (await users.SetRolesAsync(userId, [1, 99])).Error);
        Assert.Equal(AdminRoleUpdateError.UnknownUser, (await users.SetRolesAsync(12345, [1])).Error);

        await using var db = new HockeySeasonDbContext(_options);
        Assert.Equal(0, await db.UserRoles.CountAsync());
    }
    [Fact]
    public async Task GetUsers_ExposesTheAdminFlagAndGetRolesListsTheSeededRoles()
    {
        var users = new AdminUserService(new TestDbContextFactory(_options), _time);
        var adminId = await SeedUserAsync("auth0|flagged-admin", isAdmin: true);

        var listed = Assert.Single(await users.GetUsersAsync());
        Assert.True(listed.IsAdmin);
        Assert.Equal(adminId, listed.Id);
        Assert.Empty(listed.Roles);

        var roles = await users.GetRolesAsync();
        Assert.Equal(["coach", "director", "president"], roles.Select(role => role.Code));
        Assert.Equal(5, roles[0].MaxFollowedEntities);

        Assert.True(await users.IsUserAdminAsync(adminId));
        Assert.False(await users.IsUserAdminAsync(4242));
    }

    // ─── HTTP trigger guard ────────────────────────────────────────────────

    [Fact]
    public async Task ListUsers_RequiresAnAdministrator()
    {
        var service = new FakeAdminUsers { CallerIsAdmin = false };
        var function = new AdminUserFunctions(new RequestUserResolver(new FakeProvisioning()), service, WithDatabase());

        var (memberContext, request) = CreateContext(service);
        Assert.Equal(StatusCodes.Status403Forbidden,
            Assert.IsType<StatusCodeResult>(await function.ListUsers(request, memberContext, CancellationToken.None)).StatusCode);

        Assert.Equal(StatusCodes.Status401Unauthorized,
            Assert.IsType<ObjectResult>(await function.ListUsers(new DefaultHttpContext().Request, AnonymousContext(service), CancellationToken.None)).StatusCode);

        service.CallerIsAdmin = true;
        Assert.IsType<OkObjectResult>(await function.ListUsers(request, memberContext, CancellationToken.None));
    }

    [Fact]
    public async Task ListUsers_DeniesNonAdminsWithoutPersistenceInsteadOfOpeningUp()
    {
        var service = new FakeAdminUsers { CallerIsAdmin = false };
        var function = new AdminUserFunctions(new RequestUserResolver(new FakeProvisioning()), service, new ConfigurationBuilder().Build());
        var (context, request) = CreateContext(service);

        Assert.Equal(StatusCodes.Status403Forbidden,
            Assert.IsType<StatusCodeResult>(await function.ListUsers(request, context, CancellationToken.None)).StatusCode);
    }

    [Fact]
    public async Task ListUsers_AcceptsTheAuth0AdminClaimWithoutTheDatabaseFlag()
    {
        var service = new FakeAdminUsers { CallerIsAdmin = false };
        var function = new AdminUserFunctions(new RequestUserResolver(new FakeProvisioning()), service, WithDatabase());
        var (context, request) = CreateContext(service, claims: [new("roles", "admin")]);

        Assert.IsType<OkObjectResult>(await function.ListUsers(request, context, CancellationToken.None));
    }

    [Fact]
    public async Task SetUserRoles_ForwardsTheBodyAndMapsErrors()
    {
        var service = new FakeAdminUsers { CallerIsAdmin = true };
        var function = new AdminUserFunctions(new RequestUserResolver(new FakeProvisioning()), service, WithDatabase());
        var (context, request) = CreateContext(service);

        request.Body = Json("""{"roleIds":[1,3]}""");
        var ok = Assert.IsType<OkObjectResult>(await function.SetUserRoles(request, 7, context, CancellationToken.None));
        Assert.Equal([1, 3], service.LastRoleIds!);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(7, json.RootElement.GetProperty("id").GetInt32());

        request.Body = Json("{ not json");
        Assert.IsType<BadRequestObjectResult>(await function.SetUserRoles(request, 7, context, CancellationToken.None));

        service.UpdateResult = new(AdminRoleUpdateError.UnknownUser, null);
        request.Body = Json("""{"roleIds":[]}""");
        Assert.IsType<NotFoundObjectResult>(await function.SetUserRoles(request, 7, context, CancellationToken.None));

        service.UpdateResult = new(AdminRoleUpdateError.UnknownRole, null);
        request.Body = Json("""{"roleIds":[99]}""");
        Assert.IsType<BadRequestObjectResult>(await function.SetUserRoles(request, 7, context, CancellationToken.None));
    }

    // ─── Helpers ───────────────────────────────────────────────────────────

    private async Task<int> SeedUserAsync(string subject, bool isAdmin)
    {
        await using var db = new HockeySeasonDbContext(_options);
        var user = new User
        {
            Auth0UserId = subject,
            Email = $"{subject.Replace("auth0|", string.Empty)}@example.com",
            IsAdmin = isAdmin,
            CreatedAt = _time.UtcNow,
            UpdatedAt = _time.UtcNow,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static MemoryStream Json(string value) => new(Encoding.UTF8.GetBytes(value));

    /// <summary>Persistence configured: the database admin flag can be read.</summary>
    private static IConfiguration WithDatabase() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = "Host=test" })
            .Build();

    private static (FunctionContext Context, HttpRequest Request) CreateContext(
        IAdminUserService service, IEnumerable<Claim>? claims = null)
    {
        var httpContext = new DefaultHttpContext();
        var identityClaims = new List<Claim> { new(ClaimTypes.NameIdentifier, "auth0|caller") };
        identityClaims.AddRange(claims ?? []);
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(identityClaims, authenticationType: "Bearer"));
        httpContext.Request.Headers["X-Authorization"] = "Bearer test";
        return (FunctionsTests.CreateContextWith(httpContext, BuildServices(service)), httpContext.Request);
    }

    private static FunctionContext AnonymousContext(IAdminUserService service) =>
        FunctionsTests.CreateContextWith(new DefaultHttpContext(), BuildServices(service));

    private static IServiceProvider BuildServices(IAdminUserService service) =>
        new ServiceCollection()
            .AddSingleton(service)
            .AddSingleton<IUserProvisioningService, FakeProvisioning>()
            .AddScoped<RequestUserResolver>()
            .BuildServiceProvider();

    private sealed class FakeProvisioning : IUserProvisioningService
    {
        public Task<UserProvisioningResult> ProvisionAsync(ApplicationUserClaims claims, CancellationToken cancellationToken = default) =>
            Task.FromResult(new UserProvisioningResult(UserId: 7, Created: false));
    }

    private sealed class FakeAdminUsers : IAdminUserService
    {
        public bool CallerIsAdmin { get; set; }
        public AdminRoleUpdateResult UpdateResult { get; set; } =
            new(AdminRoleUpdateError.None, new AdminUserSummary(7, "auth0|caller", null, null, null, false, [], DateTimeOffset.UnixEpoch));
        public int[]? LastRoleIds { get; private set; }

        public Task<IReadOnlyList<AdminUserSummary>> GetUsersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AdminUserSummary>>([]);

        public Task<IReadOnlyList<AdminRoleOption>> GetRolesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AdminRoleOption>>([]);

        public Task<AdminRoleUpdateResult> SetRolesAsync(int userId, IReadOnlyCollection<int> roleIds, CancellationToken cancellationToken = default)
        {
            LastRoleIds = [.. roleIds];
            return Task.FromResult(UpdateResult);
        }

        public Task<bool> IsUserAdminAsync(int userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(CallerIsAdmin);
    }

    private sealed class TestDbContextFactory(DbContextOptions<HockeySeasonDbContext> options)
        : IDbContextFactory<HockeySeasonDbContext>
    {
        public HockeySeasonDbContext CreateDbContext() => new(options);
        public ValueTask<HockeySeasonDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new HockeySeasonDbContext(options));
    }
}
