using Xunit;

namespace WebSocketNotifications.Tests.Connections;

public sealed class SubscriptionRegistryTests
{
    [Fact]
    public void AddSubscription_WhenKeyIsBlank_RejectsIt()
    {
        var registry = CreateRegistry();

        Assert.Throws<ArgumentException>(() => registry.AddSubscription("connection-1", " "));
    }

    [Fact]
    public void AddSubscription_WhenConnectionHasSeveralOpaqueKeys_TracksAllSubscriptions()
    {
        var registry = CreateRegistry();
        const string group = "group:operators";
        const string feed = "feed:match-42";
        const string tenantEvent = "tenant:abc:event:score.changed";

        Assert.True(registry.AddSubscription("connection-1", group));
        Assert.True(registry.AddSubscription("connection-1", feed));
        Assert.True(registry.AddSubscription("connection-1", tenantEvent));

        Assert.Equal(
            [feed, group, tenantEvent],
            registry.GetSubscriptions("connection-1").Order());
    }

    [Fact]
    public void AddSubscription_WhenAlreadyPresent_IsIdempotent()
    {
        var registry = CreateRegistry();
        const string subscription = "group:operators";

        Assert.True(registry.AddSubscription("connection-1", subscription));
        Assert.False(registry.AddSubscription("connection-1", subscription));
        Assert.Equal([subscription], registry.GetSubscriptions("connection-1"));
    }

    [Fact]
    public void RemoveSubscription_RemovesOnlyRequestedSubscription()
    {
        var registry = CreateRegistry();
        const string group = "group:operators";
        const string feed = "feed:match-42";
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
        registry.AddSubscription("connection-1", "group:operators");

        registry.Remove("connection-1");

        Assert.Throws<KeyNotFoundException>(() => registry.GetSubscriptions("connection-1"));
    }

    [Fact]
    public void SubscriptionOperations_WhenConnectionDoesNotExist_RejectUnknownConnection()
    {
        var registry = new ConnectionRegistry();
        const string subscription = "group:operators";

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
