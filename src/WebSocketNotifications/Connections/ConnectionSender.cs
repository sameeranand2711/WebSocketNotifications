using System.Net.WebSockets;
using System.Runtime.InteropServices;

namespace WebSocketNotifications.Connections;

/// <summary>Serializes all outgoing writes for one socket to preserve accepted FIFO order.</summary>
internal sealed class ConnectionSender(WebSocket socket, ConnectionBuffer buffer)
{
    private int started;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref started, 1) != 0)
        {
            throw new InvalidOperationException("The connection send loop has already started.");
        }

        try
        {
            await foreach (var message in buffer.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                // Avoid copying the common array-backed payload while still supporting
                // arbitrary memory owners accepted by the WebSocket API.
                if (!MemoryMarshal.TryGetArray(message, out ArraySegment<byte> segment))
                {
                    segment = new ArraySegment<byte>(message.ToArray());
                }

                await socket.SendAsync(
                        segment,
                        WebSocketMessageType.Text,
                        endOfMessage: true,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
