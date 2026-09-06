using WebSocketNotifications.Contracts;

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
    private readonly Dictionary<string, HashSet<NotificationSubscription>> subscriptionsByConnection =
        new(StringComparer.Ordinal);
    private readonly Dictionary<NotificationSubscription, HashSet<string>> connectionsBySubscription = [];

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
        }
    }

    public bool Remove(string connectionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);

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

            foreach (var subscription in subscriptionsByConnection[connectionId])
            {
                RemoveFromSubscriptionIndex(connectionId, subscription);
            }

            subscriptionsByConnection.Remove(connectionId);

            return true;
        }
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

    public bool AddSubscription(string connectionId, NotificationSubscription subscription)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentNullException.ThrowIfNull(subscription);

        lock (gate)
        {
            if (!GetSubscriptionsCore(connectionId).Add(subscription))
            {
                return false;
            }

            if (!connectionsBySubscription.TryGetValue(subscription, out var subscribedConnections))
            {
                subscribedConnections = new HashSet<string>(StringComparer.Ordinal);
                connectionsBySubscription.Add(subscription, subscribedConnections);
            }

            subscribedConnections.Add(connectionId);
            return true;
        }
    }

    public bool RemoveSubscription(string connectionId, NotificationSubscription subscription)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentNullException.ThrowIfNull(subscription);

        lock (gate)
        {
            if (!GetSubscriptionsCore(connectionId).Remove(subscription))
            {
                return false;
            }

            RemoveFromSubscriptionIndex(connectionId, subscription);
            return true;
        }
    }

    public IReadOnlyList<NotificationSubscription> GetSubscriptions(string connectionId)
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
            // A connection can match several dimensions of the same notification. The set
            // guarantees that it receives only one copy from this routing operation.
            var recipients = new HashSet<string>(StringComparer.Ordinal);

            foreach (var userId in notification.UserIds)
            {
                if (connectionsByUser.TryGetValue(userId, out var userConnections))
                {
                    recipients.UnionWith(userConnections);
                }
            }

            AddSubscriptionRecipients(recipients, SubscriptionKind.Group, notification.Groups);
            AddSubscriptionRecipients(recipients, SubscriptionKind.Feed, notification.Feeds);
            AddSubscriptionRecipients(recipients, SubscriptionKind.EventType, notification.EventTypes);

            return recipients.ToArray();
        }
    }

    private HashSet<NotificationSubscription> GetSubscriptionsCore(string connectionId) =>
        subscriptionsByConnection.TryGetValue(connectionId, out var subscriptions)
            ? subscriptions
            : throw new KeyNotFoundException($"Connection '{connectionId}' is not registered.");

    private void AddSubscriptionRecipients(
        HashSet<string> recipients,
        SubscriptionKind kind,
        IReadOnlyList<string> values)
    {
        foreach (var value in values)
        {
            var subscription = new NotificationSubscription(kind, value);
            if (connectionsBySubscription.TryGetValue(subscription, out var subscribedConnections))
            {
                recipients.UnionWith(subscribedConnections);
            }
        }
    }

    private void RemoveFromSubscriptionIndex(string connectionId, NotificationSubscription subscription)
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
