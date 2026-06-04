using Jifas.Assistant.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Jifas.Assistant.Tests;

public class AdminAccessAuthorizationHandlerTests
{
    [Fact]
    public async Task HandleRequirementAsync_AllowsMatchingAdminApiKey()
    {
        var requirement = new AdminAccessRequirement();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Admin-Api-Key"] = "secret";

        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Admin:ApiKey"] = "secret"
            })
            .Build();

        var handler = new AdminAccessAuthorizationHandler(accessor, config);
        var context = new AuthorizationHandlerContext(
            new[] { requirement },
            new System.Security.Claims.ClaimsPrincipal(),
            resource: null);

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_RejectsMissingApiKey()
    {
        var requirement = new AdminAccessRequirement();
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Admin:ApiKey"] = "secret"
            })
            .Build();

        var handler = new AdminAccessAuthorizationHandler(accessor, config);
        var context = new AuthorizationHandlerContext(
            new[] { requirement },
            new System.Security.Claims.ClaimsPrincipal(),
            resource: null);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_RejectsWrongApiKey()
    {
        var requirement = new AdminAccessRequirement();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Admin-Api-Key"] = "wrong-secret";

        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Admin:ApiKey"] = "correct-secret"
            })
            .Build();

        var handler = new AdminAccessAuthorizationHandler(accessor, config);
        var context = new AuthorizationHandlerContext(
            new[] { requirement },
            new System.Security.Claims.ClaimsPrincipal(),
            resource: null);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_RejectsWhenConfiguredApiKeyIsEmpty()
    {
        var requirement = new AdminAccessRequirement();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Admin-Api-Key"] = "secret";

        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Admin:ApiKey"] = ""
            })
            .Build();

        var handler = new AdminAccessAuthorizationHandler(accessor, config);
        var context = new AuthorizationHandlerContext(
            new[] { requirement },
            new System.Security.Claims.ClaimsPrincipal(),
            resource: null);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }
}
