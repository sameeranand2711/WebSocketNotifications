namespace WebSocketNotifications.Configuration;

/// <summary>Defines how a connection reacts when its outgoing buffer is full.</summary>
public enum SlowClientPolicy
{
    /// <summary>Close and remove the slow connection.</summary>
    Disconnect,

    /// <summary>Discard the oldest buffered notification.</summary>
    DropOldest,

    /// <summary>Discard the notification currently being routed.</summary>
    DropCurrent,
}
