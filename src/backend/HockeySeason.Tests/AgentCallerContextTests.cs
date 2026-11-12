using HockeySeason.Agents;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HockeySeason.Tests;

public sealed class AgentCallerContextTests
{
    [Fact]
    public async Task RegisteredContextReadsOnlyTheCurrentRequestsMirroredToken()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHockeySeasonDataAgent(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<IHttpContextAccessor>();
        var caller = provider.GetRequiredService<AgentCallerContext>();

        Assert.Null(caller.GetAccessToken());
        var requests = new[] { "first", "second" }.Select(async token =>
        {
            var context = new DefaultHttpContext();
            context.Request.Headers.Authorization = "Bearer other-token";
            context.Request.Headers["X-Authorization"] = $"Bearer {token}";
            accessor.HttpContext = context;
            await Task.Yield();
            return caller.GetAccessToken();
        });

        Assert.Equal(new[] { "first", "second" }, await Task.WhenAll(requests));
        Assert.Null(caller.GetAccessToken());

        accessor.HttpContext = new DefaultHttpContext();
        accessor.HttpContext.Request.Headers["X-Authorization"] = "Basic invalid";
        Assert.Null(caller.GetAccessToken());
    }
}
