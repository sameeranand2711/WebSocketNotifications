using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace WebSocketNotifications.Tests.Hosting;

public sealed class ServiceRegistrationTests
{
    [Fact]
    public void AddWebSocketNotifications_WithDelegate_AppliesProgrammaticOptions()
    {
        var services = new ServiceCollection();
        services.AddWebSocketNotifications(options =>
        {
            options.EndpointPath = "/custom";
            options.HeartbeatEnabled = false;
            options.OutgoingBufferCapacity = 7;
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<WebSocketNotificationOptions>>().Value;

        Assert.Equal("/custom", options.EndpointPath);
        Assert.False(options.HeartbeatEnabled);
        Assert.Equal(7, options.OutgoingBufferCapacity);
        Assert.NotNull(provider.GetService<ConnectionRegistry>());
    }

    [Fact]
    public void AddWebSocketNotifications_WithConfiguration_BindsStandardConfigurationProvider()
    {
        var values = new Dictionary<string, string?>
        {
            ["EndpointPath"] = "/configured",
            ["CompressionEnabled"] = "true",
            ["SlowClientPolicy"] = "DropOldest",
            ["MaxIncomingMessageSize"] = "1234",
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();

        services.AddWebSocketNotifications(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<WebSocketNotificationOptions>>().Value;
        Assert.Equal("/configured", options.EndpointPath);
        Assert.True(options.CompressionEnabled);
        Assert.Equal(SlowClientPolicy.DropOldest, options.SlowClientPolicy);
        Assert.Equal(1234, options.MaxIncomingMessageSize);
    }

    [Fact]
    public void AddWebSocketNotifications_WhenConfigurationIsInvalid_DefersFailureUntilSubsystemIsUsed()
    {
        var services = new ServiceCollection();
        services.AddWebSocketNotifications(options => options.OutgoingBufferCapacity = 0);

        using var provider = services.BuildServiceProvider();

        var error = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<WebSocketNotificationOptions>>().Value);
        Assert.Contains(nameof(WebSocketNotificationOptions.OutgoingBufferCapacity), error.Failures.Single());
    }

    [Fact]
    public void AddWebSocketNotifications_AppliesConfiguredSubscriptionLimitsToRegistry()
    {
        var services = new ServiceCollection();
        services.AddWebSocketNotifications(options =>
        {
            options.MaxSubscriptionsPerConnection = 2;
            options.MaxSubscriptionKeyLength = 5;
        });
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ConnectionRegistry>();
        registry.Add("connection-1", "user-1");
        registry.AddSubscription("connection-1", "first");
        registry.AddSubscription("connection-1", "other");

        Assert.Throws<InvalidOperationException>(
            () => registry.AddSubscription("connection-1", "third"));
        Assert.Throws<ArgumentException>(
            () => registry.AddSubscription("connection-1", "123456"));
    }

    [Fact]
    public async Task AddWebSocketNotifications_WithoutApplicationAuthorizer_UsesSecureDefault()
    {
        var services = new ServiceCollection();
        services.AddWebSocketNotifications(_ => { });
        using var provider = services.BuildServiceProvider();
        var authorizer = provider.GetRequiredService<ISubscriptionAuthorizer>();

        var allowed = await authorizer.AuthorizeAsync(
            new SubscriptionAuthorizationContext(
                "connection-1",
                "user-1",
                "group:operators"),
            CancellationToken.None);

        Assert.False(allowed);
    }

    [Fact]
    public async Task AddWebSocketNotifications_WhenInvalidSubsystemIsUnused_DoesNotCrashHost()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddWebSocketNotifications(options => options.OutgoingBufferCapacity = 0);
        await using var app = builder.Build();
        app.Run(context =>
        {
            context.Response.StatusCode = 204;
            return Task.CompletedTask;
        });
        await app.StartAsync();

        var response = await app.GetTestClient().GetAsync("/");

        Assert.Equal(System.Net.HttpStatusCode.NoContent, response.StatusCode);
    }
}
