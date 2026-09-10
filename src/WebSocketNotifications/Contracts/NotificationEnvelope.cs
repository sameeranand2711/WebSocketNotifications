using System.Text.Json;

namespace WebSocketNotifications.Contracts;

/// <summary>
/// A provider-neutral notification and its local routing targets.
/// </summary>
public sealed class NotificationEnvelope
{
    /// <summary>Creates a notification envelope.</summary>
    /// <param name="messageId">An application-assigned identifier included on the wire.</param>
    /// <param name="payload">The JSON payload to clone and deliver.</param>
    /// <param name="createdAt">The UTC creation time.</param>
    /// <param name="expiresAt">The optional inclusive UTC expiry boundary.</param>
    /// <param name="userIds">Direct-user routing targets.</param>
    /// <param name="subscriptions">Opaque, application-defined subscription keys.</param>
    /// <remarks>At least one routing target is required. A connection matching several targets receives one copy.</remarks>
    public NotificationEnvelope(
        string messageId,
        JsonElement payload,
        DateTimeOffset createdAt,
        DateTimeOffset? expiresAt = null,
        IEnumerable<string>? userIds = null,
        IEnumerable<string>? subscriptions = null)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            throw new ArgumentException("A message identifier is required.", nameof(messageId));
        }
        EnsureUtc(createdAt, nameof(createdAt));

        if (expiresAt is { } expiry)
        {
            EnsureUtc(expiry, nameof(expiresAt));
        }

        if (payload.ValueKind is JsonValueKind.Undefined)
        {
            throw new ArgumentException("A JSON payload is required.", nameof(payload));
        }

        var userIdValues = CopyTargets(userIds, nameof(userIds));
        var subscriptionValues = CopyTargets(subscriptions, nameof(subscriptions));

        if (userIdValues.Count == 0 && subscriptionValues.Count == 0)
        {
            throw new ArgumentException("At least one routing target is required.");
        }

        MessageId = messageId;
        Payload = payload.Clone();
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        UserIds = userIdValues;
        Subscriptions = subscriptionValues;
    }

    /// <summary>Gets the application-assigned notification identifier.</summary>
    public string MessageId { get; }

    /// <summary>Gets the application payload as JSON.</summary>
    public JsonElement Payload { get; }

    /// <summary>Gets the UTC creation time.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Gets the optional inclusive UTC expiry time.</summary>
    public DateTimeOffset? ExpiresAt { get; }

    /// <summary>Gets direct-user routing targets.</summary>
    public IReadOnlyList<string> UserIds { get; }

    /// <summary>Gets opaque subscription keys whose meaning is owned by the consuming application.</summary>
    public IReadOnlyList<string> Subscriptions { get; }

    /// <summary>Determines whether this notification has expired at the supplied UTC time.</summary>
    /// <param name="utcNow">The UTC instant against which to compare the inclusive expiry boundary.</param>
    /// <returns><see langword="true"/> when the notification must no longer be routed.</returns>
    public bool IsExpired(DateTimeOffset utcNow) => ExpiresAt is { } expiry && expiry <= utcNow;

    private static IReadOnlyList<string> CopyTargets(IEnumerable<string>? targets, string parameterName)
    {
        if (targets is null)
        {
            return Array.Empty<string>();
        }

        var values = targets.ToArray();
        if (values.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Routing targets cannot contain blank values.", parameterName);
        }

        return Array.AsReadOnly(values);
    }

    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The timestamp must use the UTC offset.", parameterName);
        }
    }
}
