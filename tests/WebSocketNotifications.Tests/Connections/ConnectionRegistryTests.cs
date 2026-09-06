using Xunit;

namespace WebSocketNotifications.Tests.Connections;

public sealed class ConnectionRegistryTests
{
    [Fact]
    public void Add_WhenUserHasMultipleConnections_ReturnsAllConnections()
    {
        var registry = new ConnectionRegistry();

        registry.Add("connection-1", "user-1");
        registry.Add("connection-2", "user-1");

        Assert.Equal(2, registry.Count);
        Assert.Equal(
            ["connection-1", "connection-2"],
            registry.GetConnectionIdsForUser("user-1").Order());
    }

    [Fact]
    public void Remove_WhenUserHasSiblingConnection_LeavesSiblingRegistered()
    {
        var registry = new ConnectionRegistry();
        registry.Add("connection-1", "user-1");
        registry.Add("connection-2", "user-1");

        var removed = registry.Remove("connection-1");

        Assert.True(removed);
        Assert.Equal(["connection-2"], registry.GetConnectionIdsForUser("user-1"));
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void Remove_WhenLastConnectionIsRemoved_CleansUpUserMapping()
    {
        var registry = new ConnectionRegistry();
        registry.Add("connection-1", "user-1");

        Assert.True(registry.Remove("connection-1"));

        Assert.Empty(registry.GetConnectionIdsForUser("user-1"));
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void Add_WhenConnectionIdAlreadyExists_RejectsDuplicate()
    {
        var registry = new ConnectionRegistry();
        registry.Add("connection-1", "user-1");

        Assert.Throws<InvalidOperationException>(() => registry.Add("connection-1", "user-2"));
        Assert.Equal(["connection-1"], registry.GetConnectionIdsForUser("user-1"));
        Assert.Empty(registry.GetConnectionIdsForUser("user-2"));
    }

    [Fact]
    public void Remove_WhenConnectionDoesNotExist_IsIdempotent()
    {
        var registry = new ConnectionRegistry();

        Assert.False(registry.Remove("missing"));
    }

    [Fact]
    public void ConcurrentAddAndRemove_DoesNotCorruptRegistry()
    {
        var registry = new ConnectionRegistry();
        const int connectionCount = 500;

        Parallel.For(0, connectionCount, index =>
            registry.Add($"connection-{index}", $"user-{index % 10}"));
        Parallel.For(0, connectionCount, index =>
        {
            if (index % 2 == 0)
            {
                registry.Remove($"connection-{index}");
            }
        });

        Assert.Equal(connectionCount / 2, registry.Count);
        for (var userIndex = 0; userIndex < 10; userIndex++)
        {
            var expected = userIndex % 2 == 0 ? 0 : 50;
            Assert.Equal(expected, registry.GetConnectionIdsForUser($"user-{userIndex}").Count);
        }
    }
}
