using System.Text.Json;
using Xunit;

namespace WebSocketNotifications.Tests.Delivery;

public sealed class NotificationRouterTests
{
    private static readonly DateTimeOffset Now = new(2026, 2, 3, 4, 5, 6, TimeSpan.Zero);

    [Fact]
    public void ResolveRecipients_WhenUserHasMultipleConnections_ReturnsEveryConnection()
    {
        var (registry, router) = CreateRouter();
        registry.Add("connection-1", "user-1");
        registry.Add("connection-2", "user-1");
        registry.Add("connection-3", "user-2");

        var recipients = router.ResolveRecipients(CreateNotification(userIds: ["user-1"]), Now);

        Assert.Equal(["connection-1", "connection-2"], recipients.Order());
    }

    [Fact]
    public void ResolveRecipients_MatchesOpaqueSubscriptionKeys()
    {
        var (registry, router) = CreateRouter();
        AddSubscribedConnection(registry, "group-connection", "group:operators");
        AddSubscribedConnection(registry, "feed-connection", "feed:match-42");
        AddSubscribedConnection(registry, "event-connection", "tenant:abc:event:score.changed");
        AddSubscribedConnection(registry, "other-connection", "group:customers");

        var recipients = router.ResolveRecipients(
            CreateNotification(
                subscriptions: ["group:operators", "feed:match-42", "tenant:abc:event:score.changed"]),
            Now);

        Assert.Equal(
            ["event-connection", "feed-connection", "group-connection"],
            recipients.Order());
    }

    [Fact]
    public void ResolveRecipients_WhenConnectionMatchesMultipleRoutes_ReturnsItOnce()
    {
        var (registry, router) = CreateRouter();
        registry.Add("connection-1", "user-1");
        registry.AddSubscription("connection-1", "group:operators");
        registry.AddSubscription("connection-1", "event:score.changed");

        var recipients = router.ResolveRecipients(
            CreateNotification(
                userIds: ["user-1"],
                subscriptions: ["group:operators", "event:score.changed"]),
            Now);

        Assert.Equal(["connection-1"], recipients);
    }

    [Fact]
    public void ResolveRecipients_WhenNoRouteMatches_ReturnsEmptyCollection()
    {
        var (registry, router) = CreateRouter();
        AddSubscribedConnection(registry, "connection-1", "feed:other");

        var recipients = router.ResolveRecipients(CreateNotification(subscriptions: ["feed:match-42"]), Now);

        Assert.Empty(recipients);
    }

    [Fact]
    public void ResolveRecipients_WhenNotificationIsExpired_DoesNotResolveRecipients()
    {
        var (registry, router) = CreateRouter();
        registry.Add("connection-1", "user-1");

        var recipients = router.ResolveRecipients(
            CreateNotification(userIds: ["user-1"], expiresAt: Now),
            Now);

        Assert.Empty(recipients);
    }

    private static (ConnectionRegistry Registry, NotificationRouter Router) CreateRouter()
    {
        var registry = new ConnectionRegistry();
        return (registry, new NotificationRouter(registry));
    }

    private static void AddSubscribedConnection(
        ConnectionRegistry registry,
        string connectionId,
        string subscription)
    {
        registry.Add(connectionId, $"user-{connectionId}");
        registry.AddSubscription(connectionId, subscription);
    }

    private static NotificationEnvelope CreateNotification(
        IEnumerable<string>? userIds = null,
        IEnumerable<string>? subscriptions = null,
        DateTimeOffset? expiresAt = null) =>
        new(
            "message-1",
            JsonSerializer.SerializeToElement(new { value = 42 }),
            Now.AddMinutes(-1),
            expiresAt,
            userIds,
            subscriptions);
}
