using WebSocketNotifications.Contracts;
using WebSocketNotifications.Configuration;
using WebSocketNotifications.Diagnostics;

namespace WebSocketNotifications.Connections;

/// <summary>
/// Stores live connections and maintains reverse indexes for user and subscription routing.
/// </summary>
internal sealed class ConnectionRegistry
{
    // One lock protects the primary records and every reverse index. Readers return snapshots
    // so no mutable collection escapes the lock and all indexes remain mutually consistent.
    private readonly object gate = new();
    private readonly Dictionary<string, ConnectionEntry> connections = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> connectionsByUser = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> subscriptionsByConnection =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> connectionsBySubscription = new(StringComparer.Ordinal);
    private readonly WebSocketNotificationMetrics metrics;
    private readonly int maxSubscriptionsPerConnection;
    private readonly int maxSubscriptionKeyLength;

    internal int MaxSubscriptionKeyLength => maxSubscriptionKeyLength;

    public ConnectionRegistry(
        int maxSubscriptionsPerConnection = WebSocketNotificationOptions.DefaultMaxSubscriptionsPerConnection,
        int maxSubscriptionKeyLength = WebSocketNotificationOptions.DefaultMaxSubscriptionKeyLength)
        : this(WebSocketNotificationMetrics.Disabled, maxSubscriptionsPerConnection, maxSubscriptionKeyLength)
    {
    }

    internal ConnectionRegistry(
        WebSocketNotificationMetrics metrics,
        int maxSubscriptionsPerConnection,
        int maxSubscriptionKeyLength)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSubscriptionsPerConnection, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSubscriptionKeyLength, 1);
        this.metrics = metrics;
        this.maxSubscriptionsPerConnection = maxSubscriptionsPerConnection;
        this.maxSubscriptionKeyLength = maxSubscriptionKeyLength;
    }

    public int Count
    {
        get
        {
            lock (gate)
            {
                return connections.Count;
            }
        }
    }

    public void Add(string connectionId, string userId)
    {
        Add(connectionId, userId, buffer: null, requestStop: null);
    }

    public void Add(
        string connectionId,
        string userId,
        ConnectionBuffer? buffer,
        Action? requestStop)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        lock (gate)
        {
            if (!connections.TryAdd(connectionId, new ConnectionEntry(userId, buffer, requestStop)))
            {
                throw new InvalidOperationException($"Connection '{connectionId}' is already registered.");
            }

            subscriptionsByConnection.Add(connectionId, []);

            if (!connectionsByUser.TryGetValue(userId, out var userConnections))
            {
                userConnections = new HashSet<string>(StringComparer.Ordinal);
                connectionsByUser.Add(userId, userConnections);
            }

            userConnections.Add(connectionId);
            metrics.ConnectionOpened();
        }
    }

    public bool Remove(string connectionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);

        int removedSubscriptionCount;
        lock (gate)
        {
            if (!connections.Remove(connectionId, out var connection))
            {
                return false;
            }

            var userConnections = connectionsByUser[connection.UserId];
            userConnections.Remove(connectionId);
            if (userConnections.Count == 0)
            {
                connectionsByUser.Remove(connection.UserId);
            }

            var subscriptions = subscriptionsByConnection[connectionId];
            removedSubscriptionCount = subscriptions.Count;
            foreach (var subscription in subscriptions)
            {
                RemoveFromSubscriptionIndex(connectionId, subscription);
            }

            subscriptionsByConnection.Remove(connectionId);
            metrics.ConnectionClosed();
            metrics.SubscriptionsRemoved(removedSubscriptionCount);
        }
        return true;
    }

    public bool Disconnect(string connectionId)
    {
        Action? requestStop;
        lock (gate)
        {
            requestStop = connections.TryGetValue(connectionId, out var connection)
                ? connection.RequestStop
                : null;
        }

        var removed = Remove(connectionId);
        if (removed)
        {
            requestStop?.Invoke();
        }

        return removed;
    }

    public bool TryGetBuffer(string connectionId, out ConnectionBuffer? buffer)
    {
        lock (gate)
        {
            if (connections.TryGetValue(connectionId, out var connection) && connection.Buffer is not null)
            {
                buffer = connection.Buffer;
                return true;
            }

            buffer = null;
            return false;
        }
    }

    public IReadOnlyList<string> GetConnectionIdsForUser(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        lock (gate)
        {
            return connectionsByUser.TryGetValue(userId, out var userConnections)
                ? userConnections.ToArray()
                : [];
        }
    }

    public bool AddSubscription(string connectionId, string subscription)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(subscription);

        return AddSubscriptions(connectionId, [subscription]) switch
        {
            SubscriptionAddResult.Added => true,
            SubscriptionAddResult.Unchanged => false,
            SubscriptionAddResult.KeyTooLong => throw new ArgumentException(
                $"Subscription exceeds MaxSubscriptionKeyLength ({maxSubscriptionKeyLength}).",
                nameof(subscription)),
            SubscriptionAddResult.LimitExceeded => throw new InvalidOperationException(
                $"Connection has reached MaxSubscriptionsPerConnection ({maxSubscriptionsPerConnection})."),
            _ => throw new InvalidOperationException("Unsupported subscription result."),
        };
    }

    internal SubscriptionAddResult AddSubscriptions(
        string connectionId,
        IReadOnlyList<string> subscriptions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentNullException.ThrowIfNull(subscriptions);

        var addedCount = 0;
        lock (gate)
        {
            var current = GetSubscriptionsCore(connectionId);
            var additions = new List<string>(subscriptions.Count);
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (var subscription in subscriptions)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(subscription);
                if (subscription.Length > maxSubscriptionKeyLength)
                {
                    return SubscriptionAddResult.KeyTooLong;
                }

                if (!current.Contains(subscription) && unique.Add(subscription))
                {
                    additions.Add(subscription);
                }
            }

            if (additions.Count == 0)
            {
                return SubscriptionAddResult.Unchanged;
            }

            if (additions.Count > maxSubscriptionsPerConnection - current.Count)
            {
                return SubscriptionAddResult.LimitExceeded;
            }

            foreach (var subscription in additions)
            {
                current.Add(subscription);
                if (!connectionsBySubscription.TryGetValue(subscription, out var subscribedConnections))
                {
                    subscribedConnections = new HashSet<string>(StringComparer.Ordinal);
                    connectionsBySubscription.Add(subscription, subscribedConnections);
                }

                subscribedConnections.Add(connectionId);
            }

            addedCount = additions.Count;
            metrics.SubscriptionsAdded(addedCount);
        }
        return SubscriptionAddResult.Added;
    }

    public bool RemoveSubscription(string connectionId, string subscription)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(subscription);

        lock (gate)
        {
            if (!GetSubscriptionsCore(connectionId).Remove(subscription))
            {
                return false;
            }

            RemoveFromSubscriptionIndex(connectionId, subscription);
            metrics.SubscriptionsRemoved(1);
        }
        return true;
    }

    public IReadOnlyList<string> GetSubscriptions(string connectionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);

        lock (gate)
        {
            return GetSubscriptionsCore(connectionId).ToArray();
        }
    }

    public IReadOnlyList<string> GetRecipientConnectionIds(NotificationEnvelope notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        lock (gate)
        {
            // A connection can match several targets of the same notification. The set
            // guarantees that it receives only one copy from this routing operation.
            var recipients = new HashSet<string>(StringComparer.Ordinal);

            foreach (var userId in notification.UserIds)
            {
                if (connectionsByUser.TryGetValue(userId, out var userConnections))
                {
                    recipients.UnionWith(userConnections);
                }
            }

            foreach (var subscription in notification.Subscriptions)
            {
                if (connectionsBySubscription.TryGetValue(subscription, out var subscribedConnections))
                {
                    recipients.UnionWith(subscribedConnections);
                }
            }

            return recipients.ToArray();
        }
    }

    private HashSet<string> GetSubscriptionsCore(string connectionId) =>
        subscriptionsByConnection.TryGetValue(connectionId, out var subscriptions)
            ? subscriptions
            : throw new KeyNotFoundException($"Connection '{connectionId}' is not registered.");

    private void RemoveFromSubscriptionIndex(string connectionId, string subscription)
    {
        var subscribedConnections = connectionsBySubscription[subscription];
        subscribedConnections.Remove(connectionId);
        if (subscribedConnections.Count == 0)
        {
            connectionsBySubscription.Remove(subscription);
        }
    }

    private sealed record ConnectionEntry(string UserId, ConnectionBuffer? Buffer, Action? RequestStop);
}

internal enum SubscriptionAddResult
{
    Unchanged,
    Added,
    LimitExceeded,
    KeyTooLong,
}
