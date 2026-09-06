namespace WebSocketNotifications.Host.Kafka;

/// <summary>Controls the bounded handoff between Kafka workers and the notification source.</summary>
internal sealed class KafkaAdapterOptions
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "KafkaAdapter";

    /// <summary>Gets or sets the maximum number of Kafka records awaiting local routing.</summary>
    public int ChannelCapacity { get; set; } = 256;
}
