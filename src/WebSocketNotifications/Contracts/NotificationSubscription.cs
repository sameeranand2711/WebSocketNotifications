namespace WebSocketNotifications.Contracts;

/// <summary>Identifies a group, feed, or event-type subscription.</summary>
public sealed record NotificationSubscription
{
    /// <summary>Creates a subscription.</summary>
    /// <param name="kind">The routing dimension.</param>
    /// <param name="value">The nonblank, application-defined target value.</param>
    public NotificationSubscription(SubscriptionKind kind, string value)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported subscription kind.");
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A subscription value is required.", nameof(value));
        }

        Kind = kind;
        Value = value;
    }

    /// <summary>Gets the subscription dimension.</summary>
    public SubscriptionKind Kind { get; }

    /// <summary>Gets the application-defined subscription value.</summary>
    public string Value { get; }
}

/// <summary>Defines the supported client subscription dimensions.</summary>
public enum SubscriptionKind
{
    /// <summary>A group subscription.</summary>
    Group,

    /// <summary>A feed subscription.</summary>
    Feed,

    /// <summary>An event-type subscription.</summary>
    EventType,
}
