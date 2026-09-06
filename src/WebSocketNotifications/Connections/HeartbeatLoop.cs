using System.Text.Json;

namespace WebSocketNotifications.Connections;

/// <summary>Detects half-open clients with application-level ping/pong messages.</summary>
internal sealed class HeartbeatLoop(
    string connectionId,
    ConnectionRegistry registry,
    ConnectionBuffer outgoing,
    HeartbeatState state,
    TimeSpan interval,
    TimeSpan timeout,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private int started;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref started, 1) != 0)
        {
            throw new InvalidOperationException("The heartbeat loop has already started.");
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var nonce = Guid.NewGuid().ToString("N");
                var ping = JsonSerializer.SerializeToUtf8Bytes(
                    new HeartbeatPing("ping", nonce),
                    SerializerOptions);
                var result = outgoing.TryEnqueue(ping);
                if (result == BufferWriteResult.Disconnect)
                {
                    registry.Disconnect(connectionId);
                    return;
                }

                if (result == BufferWriteResult.Dropped)
                {
                    await Task.Delay(interval, timeProvider, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                // Start the timeout only after the ping was accepted by the outgoing queue.
                // A dropped ping cannot establish whether the peer is responsive.
                state.BeginProbe(nonce, timeProvider.GetUtcNow());
                await Task.Delay(timeout, timeProvider, cancellationToken).ConfigureAwait(false);

                if (state.HasTimedOut(timeProvider.GetUtcNow(), timeout))
                {
                    registry.Disconnect(connectionId);
                    return;
                }

                var remainingInterval = interval - timeout;
                if (remainingInterval > TimeSpan.Zero)
                {
                    await Task.Delay(remainingInterval, timeProvider, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private sealed record HeartbeatPing(string Type, string Nonce);
}
