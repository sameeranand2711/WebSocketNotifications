using WebSocketNotifications.Connections;
using WebSocketNotifications.Contracts;

namespace WebSocketNotifications.Delivery;

/// <summary>Applies time-sensitive routing rules before querying the connection indexes.</summary>
internal sealed class NotificationRouter(ConnectionRegistry registry)
{
    public IReadOnlyList<string> ResolveRecipients(NotificationEnvelope notification, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(notification);

        return notification.IsExpired(utcNow)
            ? []
            : registry.GetRecipientConnectionIds(notification);
    }
}
