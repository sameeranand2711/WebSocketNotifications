using System.Text.Json;
using WebSocketNotifications.Configuration;
using WebSocketNotifications.Connections;
using WebSocketNotifications.Contracts;

namespace WebSocketNotifications.Delivery;

/// <summary>Serializes and enqueues a notification for its resolved live recipients.</summary>
internal sealed class NotificationDispatcher(
    ConnectionRegistry registry,
    NotificationRouter router,
    int maxOutgoingMessageSize)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public ValueTask DispatchAsync(
        NotificationEnvelope notification,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        cancellationToken.ThrowIfCancellationRequested();

        var recipients = router.ResolveRecipients(notification, utcNow);
        if (recipients.Count == 0)
        {
            return ValueTask.CompletedTask;
        }

        // Every recipient receives identical wire JSON, so serialize once before fan-out.
        var frame = new NotificationFrame(
            "notification",
            notification.MessageId,
            notification.Payload,
            notification.CreatedAt,
            notification.ExpiresAt);
        var message = JsonSerializer.SerializeToUtf8Bytes(frame, SerializerOptions);
        if (message.Length > maxOutgoingMessageSize)
        {
            throw new InvalidOperationException(
                $"The serialized notification is {message.Length} bytes, exceeding " +
                $"{nameof(WebSocketNotificationOptions.MaxOutgoingMessageSize)} ({maxOutgoingMessageSize} bytes).");
        }

        foreach (var connectionId in recipients)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!registry.TryGetBuffer(connectionId, out var buffer) || buffer is null)
            {
                continue;
            }

            if (buffer.TryEnqueue(message) == BufferWriteResult.Disconnect)
            {
                registry.Disconnect(connectionId);
            }
        }

        return ValueTask.CompletedTask;
    }

    private sealed record NotificationFrame(
        string Type,
        string MessageId,
        JsonElement Payload,
        DateTimeOffset CreatedAt,
        DateTimeOffset? ExpiresAt);
}
