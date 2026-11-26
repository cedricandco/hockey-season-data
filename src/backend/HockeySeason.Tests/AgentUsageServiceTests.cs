using HockeySeason.Application;
using HockeySeason.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HockeySeason.Tests;

/// <summary>
/// The accounting use case over the same provider-agnostic model the host
/// uses: the question is stored once (never the answer), the usage row points
/// at it, the seeded chat agent is reused, and an unknown agent name is created
/// instead of losing the entry.
/// </summary>
public sealed class AgentUsageServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<HockeySeasonDbContext> _options;

    public AgentUsageServiceTests()
    {
        _options = new DbContextOptionsBuilder<HockeySeasonDbContext>()
            .UseSqlite(_connection)
            .Options;
    }

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        await using var db = new HockeySeasonDbContext(_options);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private AgentUsageService CreateService() =>
        new(new TestDbContextFactory(_options), NullLogger<AgentUsageService>.Instance);

    [Fact]
    public async Task Record_StoresQuestionAndUsageAgainstTheSeededAgent()
    {
        var service = CreateService();

        await service.RecordAsync(new AgentUsageRecord(
            AgentNames.HockeySeasonData,
            "Quel est le classement des Grizzlys ?",
            UserId: null,
            new AgentTokenUsage(120, 30, 150, 12)));

        await using var db = new HockeySeasonDbContext(_options);
        var agent = await db.Agents.SingleAsync();
        Assert.Equal(AgentNames.HockeySeasonData, agent.AgentName);

        var message = await db.Messages.SingleAsync();
        Assert.Equal(agent.Id, message.AgentId);
        Assert.Equal("Quel est le classement des Grizzlys ?", message.Text);

        var usage = await db.TokenUsages.SingleAsync();
        Assert.Equal(agent.Id, usage.AgentId);
        Assert.Equal(message.Id, usage.MessageId);
        Assert.Equal(120, usage.InputTokenCount);
        Assert.Equal(30, usage.OutputTokenCount);
        Assert.Equal(150, usage.TotalTokenCount);
        Assert.Equal(12, usage.ReasoningTokens);
        Assert.Null(usage.UserId);
    }

    [Fact]
    public async Task Record_WithoutQuestion_LeavesTheMessageUnset()
    {
        var service = CreateService();

        await service.RecordAsync(new AgentUsageRecord(
            AgentNames.HockeySeasonData, UserMessage: " ", UserId: null, new AgentTokenUsage(5, 1, 6, null)));

        await using var db = new HockeySeasonDbContext(_options);
        Assert.Empty(await db.Messages.ToListAsync());
        var usage = await db.TokenUsages.SingleAsync();
        Assert.Null(usage.MessageId);
        Assert.Equal(6, usage.TotalTokenCount);
    }

    [Fact]
    public async Task Record_WithOnlyAQuestion_StoresTheMessageWithoutUsage()
    {
        var service = CreateService();

        await service.RecordAsync(new AgentUsageRecord(
            AgentNames.HockeySeasonData, "Qui mène la ligue ?", UserId: null, new AgentTokenUsage(null, null, null, null)));

        await using var db = new HockeySeasonDbContext(_options);
        Assert.Equal("Qui mène la ligue ?", (await db.Messages.SingleAsync()).Text);
        Assert.Empty(await db.TokenUsages.ToListAsync());
    }

    [Fact]
    public async Task Record_WithNothingToStore_WritesNoRows()
    {
        await CreateService().RecordAsync(new AgentUsageRecord(
            AgentNames.HockeySeasonData, UserMessage: null, UserId: null, new AgentTokenUsage(null, null, null, null)));

        await using var db = new HockeySeasonDbContext(_options);
        Assert.Empty(await db.Messages.ToListAsync());
        Assert.Empty(await db.TokenUsages.ToListAsync());
        // Even the agent row is left alone: nothing was accounted.
        Assert.Single(await db.Agents.ToListAsync());
    }

    [Fact]
    public async Task Record_CreatesAnUnknownAgentInsteadOfFailing()
    {
        var service = CreateService();

        await service.RecordAsync(new AgentUsageRecord(
            "renamed.agent", "Bonjour", UserId: null, new AgentTokenUsage(1, 1, 2, null)));
        await service.RecordAsync(new AgentUsageRecord(
            "renamed.agent", "Encore", UserId: null, new AgentTokenUsage(2, 2, 4, null)));

        await using var db = new HockeySeasonDbContext(_options);
        var renamed = await db.Agents.SingleAsync(a => a.AgentName == "renamed.agent");
        Assert.Equal(2, await db.TokenUsages.CountAsync(u => u.AgentId == renamed.Id));
        Assert.Equal(2, await db.Messages.CountAsync(m => m.AgentId == renamed.Id));
        // The seeded agent is untouched.
        Assert.Equal(AgentNames.HockeySeasonData, (await db.Agents.SingleAsync(a => a.Id != renamed.Id)).AgentName);
    }

    [Fact]
    public async Task Record_LinksTheCallerWhenKnown()
    {
        int userId;
        await using (var db = new HockeySeasonDbContext(_options))
        {
            var user = new User
            {
                Auth0UserId = "auth0|usage",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            userId = user.Id;
        }

        await CreateService().RecordAsync(new AgentUsageRecord(
            AgentNames.HockeySeasonData, "Bonjour", userId, new AgentTokenUsage(1, 1, 2, null)));

        await using var check = new HockeySeasonDbContext(_options);
        Assert.Equal(userId, (await check.TokenUsages.SingleAsync()).UserId);
    }

    private sealed class TestDbContextFactory(DbContextOptions<HockeySeasonDbContext> options)
        : IDbContextFactory<HockeySeasonDbContext>
    {
        public HockeySeasonDbContext CreateDbContext() => new(options);

        public ValueTask<HockeySeasonDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new HockeySeasonDbContext(options));
    }
}
