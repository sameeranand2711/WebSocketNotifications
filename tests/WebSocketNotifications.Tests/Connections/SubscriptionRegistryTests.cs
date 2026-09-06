using Xunit;

namespace WebSocketNotifications.Tests.Connections;

public sealed class SubscriptionRegistryTests
{
    [Fact]
    public void NotificationSubscription_WhenValueOrKindIsInvalid_RejectsIt()
    {
        Assert.Throws<ArgumentException>(() => new NotificationSubscription(SubscriptionKind.Group, " "));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NotificationSubscription((SubscriptionKind)99, "value"));
    }

    [Fact]
    public void AddSubscription_WhenConnectionHasMultipleKinds_TracksAllSubscriptions()
    {
        var registry = CreateRegistry();
        var group = new NotificationSubscription(SubscriptionKind.Group, "operators");
        var feed = new NotificationSubscription(SubscriptionKind.Feed, "match-42");
        var eventType = new NotificationSubscription(SubscriptionKind.EventType, "score.changed");

        Assert.True(registry.AddSubscription("connection-1", group));
        Assert.True(registry.AddSubscription("connection-1", feed));
        Assert.True(registry.AddSubscription("connection-1", eventType));

        Assert.Equal(
            [feed, group, eventType],
            registry.GetSubscriptions("connection-1").OrderBy(subscription => subscription.Value));
    }

    [Fact]
    public void AddSubscription_WhenAlreadyPresent_IsIdempotent()
    {
        var registry = CreateRegistry();
        var subscription = new NotificationSubscription(SubscriptionKind.Group, "operators");

        Assert.True(registry.AddSubscription("connection-1", subscription));
        Assert.False(registry.AddSubscription("connection-1", subscription));
        Assert.Equal([subscription], registry.GetSubscriptions("connection-1"));
    }

    [Fact]
    public void RemoveSubscription_RemovesOnlyRequestedSubscription()
    {
        var registry = CreateRegistry();
        var group = new NotificationSubscription(SubscriptionKind.Group, "operators");
        var feed = new NotificationSubscription(SubscriptionKind.Feed, "match-42");
        registry.AddSubscription("connection-1", group);
        registry.AddSubscription("connection-1", feed);

        Assert.True(registry.RemoveSubscription("connection-1", group));
        Assert.False(registry.RemoveSubscription("connection-1", group));
        Assert.Equal([feed], registry.GetSubscriptions("connection-1"));
    }

    [Fact]
    public void Remove_WhenConnectionCloses_RemovesAllSubscriptions()
    {
        var registry = CreateRegistry();
        registry.AddSubscription(
            "connection-1",
            new NotificationSubscription(SubscriptionKind.Group, "operators"));

        registry.Remove("connection-1");

        Assert.Throws<KeyNotFoundException>(() => registry.GetSubscriptions("connection-1"));
    }

    [Fact]
    public void SubscriptionOperations_WhenConnectionDoesNotExist_RejectUnknownConnection()
    {
        var registry = new ConnectionRegistry();
        var subscription = new NotificationSubscription(SubscriptionKind.Group, "operators");

        Assert.Throws<KeyNotFoundException>(() => registry.AddSubscription("missing", subscription));
        Assert.Throws<KeyNotFoundException>(() => registry.RemoveSubscription("missing", subscription));
        Assert.Throws<KeyNotFoundException>(() => registry.GetSubscriptions("missing"));
    }

    private static ConnectionRegistry CreateRegistry()
    {
        var registry = new ConnectionRegistry();
        registry.Add("connection-1", "user-1");
        return registry;
    }
}
