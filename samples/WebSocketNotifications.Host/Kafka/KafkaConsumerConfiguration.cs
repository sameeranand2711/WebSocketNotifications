using Microsoft.Extensions.Configuration;

namespace WebSocketNotifications.Host.Kafka;

/// <summary>Applies the host process identity to its named Kafka source consumer.</summary>
internal static class KafkaConsumerConfiguration
{
    private const string ConsumersSection = "KafkaConsumerWorkers:Consumers";

    /// <summary>Configures independent, live-only Kafka consumption for this host process.</summary>
    public static void Apply(
        IConfiguration configuration,
        KafkaConsumerGroupIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(identity);

        var consumer = configuration.GetSection(ConsumersSection)
            .GetChildren()
            .SingleOrDefault(section =>
                string.Equals(
                    section["Name"],
                    KafkaNotificationConsumer.Name,
                    StringComparison.Ordinal));
        if (consumer is null)
        {
            throw new InvalidOperationException(
                $"Kafka consumer configuration '{KafkaNotificationConsumer.Name}' was not found.");
        }

        consumer["GroupId"] = identity.GroupId;
        consumer["AutoOffsetReset"] = "Latest";
    }
}
