using System.Text;
using System.Text.Json;
using Xunit;

namespace WebSocketNotifications.Tests.Delivery;

public sealed class WebSocketNotificationHubTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 6, 7, 8, 9, TimeSpan.Zero);

    [Fact]
    public void SubscribeAndUnsubscribe_ManageKnownConnection()
    {
        var registry = new ConnectionRegistry();
        registry.Add("connection-1", "user-1");
        var hub = CreateHub(registry);
        var subscription = new NotificationSubscription(SubscriptionKind.Feed, "match-42");

        Assert.True(hub.Subscribe("connection-1", subscription));
        Assert.False(hub.Subscribe("connection-1", subscription));
        Assert.True(hub.Unsubscribe("connection-1", subscription));
        Assert.Empty(registry.GetSubscriptions("connection-1"));
    }

    [Fact]
    public void SubscribeUserConnections_AppliesToAllCurrentConnectionsOnly()
    {
        var registry = new ConnectionRegistry();
        registry.Add("connection-1", "user-1");
        registry.Add("connection-2", "user-1");
        registry.Add("connection-3", "user-2");
        var subscription = new NotificationSubscription(SubscriptionKind.Group, "operators");

        var changed = CreateHub(registry).SubscribeUserConnections("user-1", subscription);

        Assert.Equal(2, changed);
        Assert.Equal([subscription], registry.GetSubscriptions("connection-1"));
        Assert.Equal([subscription], registry.GetSubscriptions("connection-2"));
        Assert.Empty(registry.GetSubscriptions("connection-3"));
    }

    [Fact]
    public void UnsubscribeUserConnections_RemovesFromAllCurrentConnectionsAndIsIdempotent()
    {
        var registry = new ConnectionRegistry();
        registry.Add("connection-1", "user-1");
        registry.Add("connection-2", "user-1");
        var subscription = new NotificationSubscription(SubscriptionKind.Group, "operators");
        registry.AddSubscription("connection-1", subscription);
        registry.AddSubscription("connection-2", subscription);
        var hub = CreateHub(registry);

        Assert.Equal(2, hub.UnsubscribeUserConnections("user-1", subscription));
        Assert.Equal(0, hub.UnsubscribeUserConnections("user-1", subscription));
        Assert.Empty(registry.GetSubscriptions("connection-1"));
        Assert.Empty(registry.GetSubscriptions("connection-2"));
    }

    [Fact]
    public async Task PublishAsync_RoutesUsingCurrentUtcTime()
    {
        var registry = new ConnectionRegistry();
        var buffer = new ConnectionBuffer(1, SlowClientPolicy.Disconnect);
        registry.Add("connection-1", "user-1", buffer, requestStop: null);
        var hub = CreateHub(registry);
        var notification = new NotificationEnvelope(
            "message-1",
            JsonSerializer.SerializeToElement(new { value = 1 }),
            Now.AddMinutes(-1),
            Now.AddMinutes(1),
            userIds: ["user-1"]);

        await hub.PublishAsync(notification);

        Assert.True(buffer.TryRead(out var message));
        Assert.Contains("message-1", Encoding.UTF8.GetString(message.Span), StringComparison.Ordinal);
    }

    private static WebSocketNotificationHub CreateHub(ConnectionRegistry registry)
    {
        var router = new NotificationRouter(registry);
        return new WebSocketNotificationHub(
            registry,
            new NotificationDispatcher(registry, router, 4096),
            new FixedTimeProvider(Now));
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
