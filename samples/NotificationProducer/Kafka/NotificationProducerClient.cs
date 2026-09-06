using System.Text.Json;
using Confluent.Kafka;
using KafkaHighThroughput.Hosting.Abstractions;
using Microsoft.Extensions.Options;
using NotificationProducer.Abstractions;
using NotificationProducer.Configuration;
using NotificationProducer.Contracts;

namespace NotificationProducer.Kafka;

/// <summary>Adapts the sample's neutral publisher boundary to KafkaHighThroughput.</summary>
internal sealed class NotificationProducerClient(IOptions<NotificationProducerOptions> options)
    : KafkaProducerClientBase<string, string>, INotificationPublisher
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public override string ProducerName => options.Value.ProducerName;

    public async Task<NotificationPublishReceipt> PublishAsync(
        QueuedNotification notification,
        CancellationToken cancellationToken)
    {
        var producer = await GetProducerAsync(cancellationToken);
        var json = JsonSerializer.Serialize(notification, SerializerOptions);
        // Waiting for the delivery report lets the API return an authoritative topic position.
        // It does not imply that the downstream WebSocket client received the notification.
        var result = await producer.ProduceAndWaitForDeliveryAsync(
            options.Value.Topic,
            notification.Key,
            json,
            cancellationToken);
        return new NotificationPublishReceipt(
            result.Topic,
            result.Partition.Value,
            result.Offset.Value);
    }
}
