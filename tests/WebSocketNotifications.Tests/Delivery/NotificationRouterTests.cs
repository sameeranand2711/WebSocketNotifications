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
    public void ResolveRecipients_MatchesGroupFeedAndEventTypeSubscriptions()
    {
        var (registry, router) = CreateRouter();
        AddSubscribedConnection(registry, "group-connection", SubscriptionKind.Group, "operators");
        AddSubscribedConnection(registry, "feed-connection", SubscriptionKind.Feed, "match-42");
        AddSubscribedConnection(registry, "event-connection", SubscriptionKind.EventType, "score.changed");
        AddSubscribedConnection(registry, "other-connection", SubscriptionKind.Group, "customers");

        var recipients = router.ResolveRecipients(
            CreateNotification(
                groups: ["operators"],
                feeds: ["match-42"],
                eventTypes: ["score.changed"]),
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
        registry.AddSubscription(
            "connection-1",
            new NotificationSubscription(SubscriptionKind.Group, "operators"));
        registry.AddSubscription(
            "connection-1",
            new NotificationSubscription(SubscriptionKind.EventType, "score.changed"));

        var recipients = router.ResolveRecipients(
            CreateNotification(
                userIds: ["user-1"],
                groups: ["operators"],
                eventTypes: ["score.changed"]),
            Now);

        Assert.Equal(["connection-1"], recipients);
    }

    [Fact]
    public void ResolveRecipients_WhenNoRouteMatches_ReturnsEmptyCollection()
    {
        var (registry, router) = CreateRouter();
        AddSubscribedConnection(registry, "connection-1", SubscriptionKind.Feed, "other-feed");

        var recipients = router.ResolveRecipients(CreateNotification(feeds: ["match-42"]), Now);

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
        SubscriptionKind kind,
        string value)
    {
        registry.Add(connectionId, $"user-{connectionId}");
        registry.AddSubscription(connectionId, new NotificationSubscription(kind, value));
    }

    private static NotificationEnvelope CreateNotification(
        IEnumerable<string>? userIds = null,
        IEnumerable<string>? groups = null,
        IEnumerable<string>? feeds = null,
        IEnumerable<string>? eventTypes = null,
        DateTimeOffset? expiresAt = null) =>
        new(
            "message-1",
            JsonSerializer.SerializeToElement(new { value = 42 }),
            Now.AddMinutes(-1),
            expiresAt,
            userIds,
            groups,
            feeds,
            eventTypes);
}
