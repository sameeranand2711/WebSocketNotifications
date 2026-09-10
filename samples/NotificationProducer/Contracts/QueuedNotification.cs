using System.Text.Json;
using System.Text.Json.Serialization;

namespace NotificationProducer.Contracts;

/// <summary>Matches the provider-neutral JSON envelope consumed by the host sample.</summary>
internal sealed record QueuedNotification(
    string MessageId,
    // The key controls Kafka partitioning but is transport metadata, not notification JSON.
    [property: JsonIgnore] string Key,
    string[] UserIds,
    string[] Subscriptions,
    JsonElement Payload,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt);
