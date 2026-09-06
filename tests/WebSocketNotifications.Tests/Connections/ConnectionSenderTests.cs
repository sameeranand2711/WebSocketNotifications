using System.Net.WebSockets;
using System.Text;
using Xunit;

namespace WebSocketNotifications.Tests.Connections;

public sealed class ConnectionSenderTests
{
    [Fact]
    public async Task RunAsync_SendsBufferedMessagesInOrderWithoutConcurrentWrites()
    {
        var socket = new RecordingWebSocket();
        var buffer = new ConnectionBuffer(3, SlowClientPolicy.Disconnect);
        buffer.TryEnqueue(Bytes("first"));
        buffer.TryEnqueue(Bytes("second"));
        buffer.TryEnqueue(Bytes("third"));
        buffer.Complete();

        await new ConnectionSender(socket, buffer).RunAsync(CancellationToken.None);

        Assert.Equal(["first", "second", "third"], socket.Messages);
        Assert.Equal(1, socket.MaximumConcurrentSends);
    }

    [Fact]
    public async Task RunAsync_WhenSecondLoopStarts_RejectsSecondSender()
    {
        var socket = new RecordingWebSocket();
        var buffer = new ConnectionBuffer(1, SlowClientPolicy.Disconnect);
        var sender = new ConnectionSender(socket, buffer);
        using var cancellation = new CancellationTokenSource();
        var firstLoop = sender.RunAsync(cancellation.Token);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sender.RunAsync(CancellationToken.None));

        await cancellation.CancelAsync();
        await firstLoop;
    }

    [Fact]
    public async Task RunAsync_WhenSendFails_PropagatesFailureWithoutRetry()
    {
        var socket = new RecordingWebSocket { SendFailure = new WebSocketException("send failed") };
        var buffer = new ConnectionBuffer(1, SlowClientPolicy.Disconnect);
        buffer.TryEnqueue(Bytes("notification"));
        buffer.Complete();

        await Assert.ThrowsAsync<WebSocketException>(
            () => new ConnectionSender(socket, buffer).RunAsync(CancellationToken.None));
        Assert.Equal(1, socket.SendAttempts);
    }

    [Fact]
    public async Task RunAsync_WhenCancelledWithoutMessages_CompletesNormally()
    {
        var socket = new RecordingWebSocket();
        var buffer = new ConnectionBuffer(1, SlowClientPolicy.Disconnect);
        using var cancellation = new CancellationTokenSource();
        var loop = new ConnectionSender(socket, buffer).RunAsync(cancellation.Token);

        await cancellation.CancelAsync();
        await loop;

        Assert.Empty(socket.Messages);
    }

    private static ReadOnlyMemory<byte> Bytes(string value) => Encoding.UTF8.GetBytes(value);

    private sealed class RecordingWebSocket : WebSocket
    {
        private readonly List<string> messages = [];
        private int activeSends;

        public Exception? SendFailure { get; init; }

        public int SendAttempts { get; private set; }

        public int MaximumConcurrentSends { get; private set; }

        public IReadOnlyList<string> Messages => messages;

        public override WebSocketCloseStatus? CloseStatus => null;

        public override string? CloseStatusDescription => null;

        public override WebSocketState State => WebSocketState.Open;

        public override string? SubProtocol => null;

        public override void Abort()
        {
        }

        public override Task CloseAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public override Task CloseOutputAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public override void Dispose()
        {
        }

        public override Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public override async Task SendAsync(
            ArraySegment<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken)
        {
            SendAttempts++;
            var concurrent = Interlocked.Increment(ref activeSends);
            MaximumConcurrentSends = Math.Max(MaximumConcurrentSends, concurrent);
            try
            {
                await Task.Yield();
                if (SendFailure is not null)
                {
                    throw SendFailure;
                }

                messages.Add(Encoding.UTF8.GetString(buffer));
            }
            finally
            {
                Interlocked.Decrement(ref activeSends);
            }
        }
    }
}
