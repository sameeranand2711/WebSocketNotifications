using System.Text.Json;

namespace NotificationProducer.Contracts;

/// <summary>Requests delivery along one routing dimension.</summary>
public sealed class TargetedNotificationRequest
{
    /// <summary>Gets the user IDs, groups, feeds, or event types targeted by the selected endpoint.</summary>
    public string[] Targets { get; init; } = [];

    /// <summary>Gets the application-defined JSON delivered to matching clients.</summary>
    public JsonElement Payload { get; init; }

    /// <summary>Gets the optional inclusive UTC expiry boundary.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Gets the optional Kafka key used to select the ordering partition.</summary>
    public string? Key { get; init; }
}

/// <summary>Requests one notification routed through any combination of supported dimensions.</summary>
public sealed class CombinedNotificationRequest
{
    /// <summary>Gets direct-user targets.</summary>
    public string[] UserIds { get; init; } = [];

    /// <summary>Gets group targets.</summary>
    public string[] Groups { get; init; } = [];

    /// <summary>Gets feed targets.</summary>
    public string[] Feeds { get; init; } = [];

    /// <summary>Gets event-type targets.</summary>
    public string[] EventTypes { get; init; } = [];

    /// <summary>Gets the application-defined JSON delivered to matching clients.</summary>
    public JsonElement Payload { get; init; }

    /// <summary>Gets the optional inclusive UTC expiry boundary.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Gets the optional Kafka key used to select the ordering partition.</summary>
    public string? Key { get; init; }
}

/// <summary>Describes the broker acknowledgement returned after a successful publish.</summary>
/// <param name="MessageId">The generated notification identifier placed in the envelope.</param>
/// <param name="Key">The explicit or derived Kafka ordering key.</param>
/// <param name="Topic">The Kafka topic that accepted the record.</param>
/// <param name="Partition">The assigned Kafka partition.</param>
/// <param name="Offset">The assigned Kafka offset.</param>
public sealed record PublishNotificationResponse(
    string MessageId,
    string Key,
    string Topic,
    int Partition,
    long Offset);
