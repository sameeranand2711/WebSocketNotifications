using NotificationProducer.Contracts;

namespace NotificationProducer.Abstractions;

/// <summary>Separates the HTTP sample from the concrete Kafka producer implementation.</summary>
internal interface INotificationPublisher
{
    /// <summary>Publishes one serialized notification and waits for the broker delivery result.</summary>
    Task<NotificationPublishReceipt> PublishAsync(
        QueuedNotification notification,
        CancellationToken cancellationToken);
}

/// <summary>Captures the broker position assigned to a successfully produced notification.</summary>
internal sealed record NotificationPublishReceipt(string Topic, int Partition, long Offset);
