using WebSocketNotifications.Connections;
using WebSocketNotifications.Contracts;

namespace WebSocketNotifications.Delivery;

/// <summary>Publishes notifications and manages live in-memory subscriptions programmatically.</summary>
public sealed class WebSocketNotificationHub
{
    private readonly ConnectionRegistry registry;
    private readonly NotificationDispatcher dispatcher;
    private readonly TimeProvider timeProvider;

    internal WebSocketNotificationHub(
        ConnectionRegistry registry,
        NotificationDispatcher dispatcher,
        TimeProvider timeProvider)
    {
        this.registry = registry;
        this.dispatcher = dispatcher;
        this.timeProvider = timeProvider;
    }

    /// <summary>Accepts a notification into the bounded queues of its currently connected recipients.</summary>
    /// <param name="notification">The notification and its routing targets.</param>
    /// <param name="cancellationToken">Cancels local routing before it completes.</param>
    /// <returns>A value task that completes after local routing, not after client acknowledgement.</returns>
    /// <remarks>
    /// Completion does not imply that a client processed the notification. Slow-client policy may drop
    /// a notification or disconnect a recipient while other recipients continue normally.
    /// </remarks>
    public ValueTask PublishAsync(
        NotificationEnvelope notification,
        CancellationToken cancellationToken = default) =>
        dispatcher.DispatchAsync(notification, timeProvider.GetUtcNow(), cancellationToken);

    /// <summary>Adds a subscription to one connection.</summary>
    /// <param name="connectionId">The live connection identifier.</param>
    /// <param name="subscription">The subscription to add.</param>
    /// <returns><see langword="true"/> when the registry changed; otherwise <see langword="false"/>.</returns>
    /// <remarks>This programmatic API trusts the caller and does not invoke <c>ISubscriptionAuthorizer</c>.</remarks>
    public bool Subscribe(string connectionId, string subscription)
        => registry.AddSubscription(connectionId, subscription);

    /// <summary>Removes a subscription from one connection.</summary>
    /// <param name="connectionId">The live connection identifier.</param>
    /// <param name="subscription">The subscription to remove.</param>
    /// <returns><see langword="true"/> when the registry changed; otherwise <see langword="false"/>.</returns>
    public bool Unsubscribe(string connectionId, string subscription)
        => registry.RemoveSubscription(connectionId, subscription);

    /// <summary>Adds a subscription to every current connection for a user.</summary>
    /// <param name="userId">The application user identifier.</param>
    /// <param name="subscription">The subscription to add.</param>
    /// <returns>The number of live connection records changed.</returns>
    /// <remarks>Connections opened after this call are not affected.</remarks>
    public int SubscribeUserConnections(string userId, string subscription)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(subscription);

        var changed = 0;
        foreach (var connectionId in registry.GetConnectionIdsForUser(userId))
        {
            try
            {
                if (registry.AddSubscription(connectionId, subscription))
                {
                    changed++;
                }
            }
            catch (KeyNotFoundException)
            {
                // The connection closed after the user snapshot was taken.
            }
        }

        return changed;
    }

    /// <summary>Removes a subscription from every current connection for a user.</summary>
    /// <param name="userId">The application user identifier.</param>
    /// <param name="subscription">The subscription to remove.</param>
    /// <returns>The number of live connection records changed.</returns>
    /// <remarks>Connections opened after this call are not affected.</remarks>
    public int UnsubscribeUserConnections(string userId, string subscription)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(subscription);

        var changed = 0;
        foreach (var connectionId in registry.GetConnectionIdsForUser(userId))
        {
            try
            {
                if (registry.RemoveSubscription(connectionId, subscription))
                {
                    changed++;
                }
            }
            catch (KeyNotFoundException)
            {
                // The connection closed after the user snapshot was taken.
            }
        }

        return changed;
    }
}
