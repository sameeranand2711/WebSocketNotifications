using System.Buffers;
using System.Net.WebSockets;
using WebSocketNotifications.Protocol;

namespace WebSocketNotifications.Connections;

/// <summary>Runs the single receive loop for a connection and assembles fragmented text messages.</summary>
internal sealed class ConnectionReceiver(
    WebSocket socket,
    ProtocolProcessor processor,
    string connectionId,
    string userId,
    ConnectionBuffer outgoing,
    int maxIncomingMessageSize)
{
    private const int ReceiveBufferSize = 4 * 1024;
    private int started;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref started, 1) != 0)
        {
            throw new InvalidOperationException("The connection receive loop has already started.");
        }

        var receiveBuffer = ArrayPool<byte>.Shared.Rent(ReceiveBufferSize);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // A WebSocket message may span several frames. Apply the size limit while
                // assembling it so an attacker cannot force an unbounded intermediate buffer.
                var message = new ArrayBufferWriter<byte>();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(receiveBuffer, cancellationToken).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await socket.CloseOutputAsync(
                                result.CloseStatus ?? WebSocketCloseStatus.NormalClosure,
                                result.CloseStatusDescription,
                                cancellationToken)
                            .ConfigureAwait(false);
                        return;
                    }

                    if (result.MessageType != WebSocketMessageType.Text)
                    {
                        await socket.CloseOutputAsync(
                                WebSocketCloseStatus.InvalidMessageType,
                                "Only JSON text messages are supported.",
                                cancellationToken)
                            .ConfigureAwait(false);
                        return;
                    }

                    if (result.Count > maxIncomingMessageSize - message.WrittenCount)
                    {
                        await socket.CloseOutputAsync(
                                WebSocketCloseStatus.MessageTooBig,
                                $"The message exceeded {maxIncomingMessageSize} bytes.",
                                cancellationToken)
                            .ConfigureAwait(false);
                        return;
                    }

                    message.Write(receiveBuffer.AsSpan(0, result.Count));
                }
                while (!result.EndOfMessage);

                await processor.ProcessAsync(
                        connectionId,
                        userId,
                        message.WrittenMemory,
                        outgoing,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(receiveBuffer);
        }
    }
}
