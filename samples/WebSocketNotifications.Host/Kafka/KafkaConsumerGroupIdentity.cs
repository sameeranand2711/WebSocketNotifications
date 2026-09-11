namespace WebSocketNotifications.Host.Kafka;

/// <summary>Builds the per-process Kafka consumer-group identity used for source fan-out.</summary>
internal sealed record KafkaConsumerGroupIdentity(string InstanceId, string GroupId)
{
    /// <summary>Creates an application- and environment-namespaced consumer-group identity.</summary>
    public static KafkaConsumerGroupIdentity Create(
        KafkaAdapterOptions options,
        string environmentName,
        Func<string>? ephemeralInstanceIdFactory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ApplicationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);

        var instanceId = string.IsNullOrWhiteSpace(options.InstanceId)
            ? (ephemeralInstanceIdFactory ?? CreateEphemeralInstanceId)()
            : options.InstanceId;
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

        return new KafkaConsumerGroupIdentity(
            instanceId,
            $"{options.ApplicationName}.{environmentName}.{instanceId}");
    }

    private static string CreateEphemeralInstanceId() => Guid.NewGuid().ToString("N");
}
