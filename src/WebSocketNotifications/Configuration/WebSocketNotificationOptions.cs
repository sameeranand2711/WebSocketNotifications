namespace WebSocketNotifications.Configuration;

/// <summary>Configures the WebSocket notification subsystem.</summary>
public sealed class WebSocketNotificationOptions
{
    internal const int DefaultMaxSubscriptionsPerConnection = 128;
    internal const int DefaultMaxSubscriptionKeyLength = 256;

    /// <summary>Gets the conventional configuration section name.</summary>
    public const string SectionName = "WebSocketNotifications";

    /// <summary>Gets or sets the mapped WebSocket endpoint path.</summary>
    public string EndpointPath { get; set; } = "/ws/notifications";

    /// <summary>Gets or sets whether application-level heartbeat checks are enabled.</summary>
    public bool HeartbeatEnabled { get; set; } = true;

    /// <summary>Gets or sets how often heartbeat probes are emitted. The default is 30 seconds.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets how long a heartbeat response may take. The default is 10 seconds.</summary>
    public TimeSpan HeartbeatTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Gets or sets whether WebSocket response compression is enabled.</summary>
    public bool CompressionEnabled { get; set; }

    /// <summary>Gets or sets the maximum assembled inbound text-message size in bytes. The default is 64 KiB.</summary>
    public int MaxIncomingMessageSize { get; set; } = 64 * 1024;

    /// <summary>Gets or sets the maximum outbound text-message size in bytes. The default is 256 KiB.</summary>
    public int MaxOutgoingMessageSize { get; set; } = 256 * 1024;

    /// <summary>Gets or sets the bounded outgoing notification capacity per connection. The default is 128.</summary>
    public int OutgoingBufferCapacity { get; set; } = 128;

    /// <summary>Gets or sets the maximum subscriptions held by one connection. The default is 128.</summary>
    public int MaxSubscriptionsPerConnection { get; set; } = DefaultMaxSubscriptionsPerConnection;

    /// <summary>Gets or sets the maximum length of one subscription key. The default is 256 characters.</summary>
    public int MaxSubscriptionKeyLength { get; set; } = DefaultMaxSubscriptionKeyLength;

    /// <summary>Gets or sets the full-buffer behavior.</summary>
    public SlowClientPolicy SlowClientPolicy { get; set; }
}
