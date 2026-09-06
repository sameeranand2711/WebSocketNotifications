namespace NotificationProducer.Configuration;

/// <summary>Configures the Kafka producer selected by the sample API.</summary>
internal sealed class NotificationProducerOptions
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "NotificationProducer";

    /// <summary>Gets or sets the named KafkaHighThroughput producer registration.</summary>
    public string ProducerName { get; set; } = "notification-producer";

    /// <summary>Gets or sets the Kafka topic that carries notification envelopes.</summary>
    public string Topic { get; set; } = "notifications";
}
