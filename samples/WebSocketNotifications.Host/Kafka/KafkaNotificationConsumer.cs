using System.Text.Json;
using Confluent.Kafka;
using KafkaHighThroughput.Hosting.Abstractions;
using WebSocketNotifications.Contracts;

namespace WebSocketNotifications.Host.Kafka;

/// <summary>Deserializes Kafka records and submits neutral envelopes to the host message source.</summary>
internal sealed class KafkaNotificationConsumer(KafkaNotificationMessageSource source)
    : IKafkaTopicConsumer<string, string>
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public string ConsumerName => "websocket-notifications";

    public async Task HandleAsync(
        ConsumeResult<string, string> message,
        CancellationToken cancellationToken)
    {
        var queued = JsonSerializer.Deserialize<QueuedNotification>(message.Message.Value, SerializerOptions)
            ?? throw new JsonException("Kafka notification payload was null.");
        // SubmitAsync completes only after the library accepts local routing, allowing the
        // Kafka worker to align its provider-owned acknowledgement with that boundary.
        await source.SubmitAsync(queued.ToEnvelope(), cancellationToken);
    }

    private sealed record QueuedNotification(
        string MessageId,
        string[]? UserIds,
        string[]? Subscriptions,
        JsonElement Payload,
        DateTimeOffset CreatedAt,
        DateTimeOffset? ExpiresAt)
    {
        public NotificationEnvelope ToEnvelope() =>
            new(
                MessageId,
                Payload,
                CreatedAt,
                ExpiresAt,
                UserIds,
                Subscriptions);
    }
}
