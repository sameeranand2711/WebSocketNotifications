namespace WebSocketNotifications.Connections;

/// <summary>Synchronizes heartbeat state shared by the send timer and receive loop.</summary>
internal sealed class HeartbeatState
{
    private readonly object gate = new();
    private string? pendingNonce;
    private DateTimeOffset pendingSince;

    public void BeginProbe(string nonce, DateTimeOffset sentAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nonce);
        lock (gate)
        {
            pendingNonce = nonce;
            pendingSince = sentAt;
        }
    }

    public bool Acknowledge(string nonce)
    {
        lock (gate)
        {
            if (!string.Equals(nonce, pendingNonce, StringComparison.Ordinal))
            {
                return false;
            }

            pendingNonce = null;
            return true;
        }
    }

    public bool HasTimedOut(DateTimeOffset utcNow, TimeSpan timeout)
    {
        lock (gate)
        {
            return pendingNonce is not null && utcNow - pendingSince >= timeout;
        }
    }
}
