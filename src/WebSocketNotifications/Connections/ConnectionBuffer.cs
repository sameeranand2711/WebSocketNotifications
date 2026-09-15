using System.Runtime.CompilerServices;
using System.Threading.Channels;
using WebSocketNotifications.Configuration;
using WebSocketNotifications.Diagnostics;

namespace WebSocketNotifications.Connections;

/// <summary>
/// Owns the bounded, multi-writer/single-reader queue for one WebSocket connection.
/// </summary>
internal sealed class ConnectionBuffer
{
    private readonly Channel<ReadOnlyMemory<byte>> channel;
    private readonly WebSocketNotificationMetrics metrics;

    public ConnectionBuffer(int capacity, SlowClientPolicy policy)
        : this(capacity, policy, WebSocketNotificationMetrics.Disabled)
    {
    }

    internal ConnectionBuffer(
        int capacity,
        SlowClientPolicy policy,
        WebSocketNotificationMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        if (!Enum.IsDefined(policy))
        {
            throw new ArgumentOutOfRangeException(nameof(policy), policy, "Unsupported slow-client policy.");
        }

        Policy = policy;
        this.metrics = metrics;
        // Disconnect and DropCurrent need TryWrite to report a full queue, so they use
        // Wait mode without ever performing a waiting write. DropOldest is delegated to
        // the channel because replacement must be atomic with concurrent publishers.
        var options = new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = policy == SlowClientPolicy.DropOldest
                ? BoundedChannelFullMode.DropOldest
                : BoundedChannelFullMode.Wait,
        };
        channel = policy == SlowClientPolicy.DropOldest
            ? Channel.CreateBounded<ReadOnlyMemory<byte>>(
                options,
                _ => metrics.QueuedMessageDropped())
            : Channel.CreateBounded<ReadOnlyMemory<byte>>(options);
    }

    public SlowClientPolicy Policy { get; }

    public BufferWriteResult TryEnqueue(ReadOnlyMemory<byte> message)
    {
        if (channel.Writer.TryWrite(message))
        {
            metrics.MessageEnqueued();
            return BufferWriteResult.Enqueued;
        }

        if (Policy == SlowClientPolicy.Disconnect)
        {
            metrics.SlowClientDisconnected();
            return BufferWriteResult.Disconnect;
        }

        metrics.SlowClientMessageDropped();
        return BufferWriteResult.Dropped;
    }

    public async ValueTask<ReadOnlyMemory<byte>> ReadAsync(CancellationToken cancellationToken = default)
    {
        var message = await channel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        metrics.MessageDequeued();
        return message;
    }

    public bool TryRead(out ReadOnlyMemory<byte> message)
    {
        if (!channel.Reader.TryRead(out message))
        {
            return false;
        }

        metrics.MessageDequeued();
        return true;
    }

    public async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var message in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            metrics.MessageDequeued();
            yield return message;
        }
    }

    public void Complete() => channel.Writer.TryComplete();

    public void DiscardPending()
    {
        while (TryRead(out _))
        {
        }
    }
}

internal enum BufferWriteResult
{
    Enqueued,
    Dropped,
    Disconnect,
}
