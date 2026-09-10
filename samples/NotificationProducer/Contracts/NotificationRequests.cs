using System.Text.Json;

namespace NotificationProducer.Contracts;

/// <summary>Requests delivery to one or more direct users.</summary>
public sealed class UserNotificationRequest
{
    /// <summary>Gets the direct user IDs that should receive the notification.</summary>
    public string[] Users { get; init; } = [];

    /// <summary>Gets the application-defined JSON delivered to matching clients.</summary>
    public JsonElement Payload { get; init; }

    /// <summary>Gets the optional inclusive UTC expiry boundary.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Gets the optional Kafka key used to select the ordering partition.</summary>
    public string? Key { get; init; }
}

/// <summary>Requests delivery to one or more opaque subscription keys.</summary>
public sealed class SubscriptionNotificationRequest
{
    /// <summary>Gets the opaque subscription keys that should receive the notification.</summary>
    public string[] Subscriptions { get; init; } = [];

    /// <summary>Gets the application-defined JSON delivered to matching clients.</summary>
    public JsonElement Payload { get; init; }

    /// <summary>Gets the optional inclusive UTC expiry boundary.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Gets the optional Kafka key used to select the ordering partition.</summary>
    public string? Key { get; init; }
}

/// <summary>Requests one notification routed to direct users, opaque subscriptions, or both.</summary>
public sealed class CombinedNotificationRequest
{
    /// <summary>Gets direct-user targets.</summary>
    public string[] UserIds { get; init; } = [];

    /// <summary>Gets opaque, application-defined subscription keys.</summary>
    public string[] Subscriptions { get; init; } = [];

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
