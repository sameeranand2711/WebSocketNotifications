namespace WebSocketNotifications.Host.Kafka;

/// <summary>Controls the bounded handoff between Kafka workers and the notification source.</summary>
internal sealed class KafkaAdapterOptions
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "KafkaAdapter";

    /// <summary>Gets or sets the maximum number of Kafka records awaiting local routing.</summary>
    public int ChannelCapacity { get; set; } = 256;

    /// <summary>Gets or sets the application namespace used in the Kafka consumer group.</summary>
    public string ApplicationName { get; set; } = "websocket-notifications";

    /// <summary>
    /// Gets or sets the optional identity of this process incarnation. When omitted, the
    /// sample generates an ephemeral identity at startup.
    /// </summary>
    public string? InstanceId { get; set; }
}
