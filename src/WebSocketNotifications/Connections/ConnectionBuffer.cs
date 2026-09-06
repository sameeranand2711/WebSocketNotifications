using System.Threading.Channels;
using WebSocketNotifications.Configuration;

namespace WebSocketNotifications.Connections;

/// <summary>
/// Owns the bounded, multi-writer/single-reader queue for one WebSocket connection.
/// </summary>
internal sealed class ConnectionBuffer
{
    private readonly Channel<ReadOnlyMemory<byte>> channel;

    public ConnectionBuffer(int capacity, SlowClientPolicy policy)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        if (!Enum.IsDefined(policy))
        {
            throw new ArgumentOutOfRangeException(nameof(policy), policy, "Unsupported slow-client policy.");
        }

        Policy = policy;
        // Disconnect and DropCurrent need TryWrite to report a full queue, so they use
        // Wait mode without ever performing a waiting write. DropOldest is delegated to
        // the channel because replacement must be atomic with concurrent publishers.
        channel = Channel.CreateBounded<ReadOnlyMemory<byte>>(
            new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = policy == SlowClientPolicy.DropOldest
                    ? BoundedChannelFullMode.DropOldest
                    : BoundedChannelFullMode.Wait,
            });
    }

    public SlowClientPolicy Policy { get; }

    public BufferWriteResult TryEnqueue(ReadOnlyMemory<byte> message)
    {
        if (channel.Writer.TryWrite(message))
        {
            return BufferWriteResult.Enqueued;
        }

        return Policy == SlowClientPolicy.Disconnect
            ? BufferWriteResult.Disconnect
            : BufferWriteResult.Dropped;
    }

    public ValueTask<ReadOnlyMemory<byte>> ReadAsync(CancellationToken cancellationToken = default) =>
        channel.Reader.ReadAsync(cancellationToken);

    public bool TryRead(out ReadOnlyMemory<byte> message) => channel.Reader.TryRead(out message);

    public IAsyncEnumerable<ReadOnlyMemory<byte>> ReadAllAsync(CancellationToken cancellationToken = default) =>
        channel.Reader.ReadAllAsync(cancellationToken);

    public void Complete() => channel.Writer.TryComplete();
}

internal enum BufferWriteResult
{
    Enqueued,
    Dropped,
    Disconnect,
}
