using System.Net.WebSockets;
using System.Text;
using Xunit;

namespace WebSocketNotifications.Tests.Connections;

public sealed class ConnectionReceiverTests
{
    [Fact]
    public async Task RunAsync_WhenTextMessageIsFragmented_ProcessesCompleteCommand()
    {
        var socket = new ScriptedWebSocket();
        socket.AddText("{\"type\":\"subscribe\",\"requestId\":\"r1\",", endOfMessage: false);
        socket.AddText("\"subscriptions\":[\"group:operators\"]}", endOfMessage: true);
        socket.AddClose();
        var (registry, receiver) = CreateReceiver(socket, maxIncomingMessageSize: 1024);

        await receiver.RunAsync(CancellationToken.None);

        Assert.Equal(
            ["group:operators"],
            registry.GetSubscriptions("connection-1"));
    }

    [Fact]
    public async Task RunAsync_WhenMessageExceedsConfiguredLimit_ClosesWithMessageTooBig()
    {
        var socket = new ScriptedWebSocket();
        socket.AddText(new string('x', 33), endOfMessage: true);
        var (_, receiver) = CreateReceiver(socket, maxIncomingMessageSize: 32);

        await receiver.RunAsync(CancellationToken.None);

        Assert.Equal(WebSocketCloseStatus.MessageTooBig, socket.OutputCloseStatus);
    }

    [Fact]
    public async Task RunAsync_WhenPeerSendsClose_CompletesCloseHandshake()
    {
        var socket = new ScriptedWebSocket();
        socket.AddClose(WebSocketCloseStatus.NormalClosure, "done");
        var (_, receiver) = CreateReceiver(socket, maxIncomingMessageSize: 1024);

        await receiver.RunAsync(CancellationToken.None);

        Assert.Equal(WebSocketCloseStatus.NormalClosure, socket.OutputCloseStatus);
        Assert.Equal("done", socket.OutputCloseDescription);
    }

    [Fact]
    public async Task RunAsync_WhenPeerSendsBinaryMessage_ClosesAsInvalidMessageType()
    {
        var socket = new ScriptedWebSocket();
        socket.AddBinary([1, 2, 3]);
        var (_, receiver) = CreateReceiver(socket, maxIncomingMessageSize: 1024);

        await receiver.RunAsync(CancellationToken.None);

        Assert.Equal(WebSocketCloseStatus.InvalidMessageType, socket.OutputCloseStatus);
    }

    [Fact]
    public async Task RunAsync_WhenReceiveFails_PropagatesFailure()
    {
        var socket = new ScriptedWebSocket { ReceiveFailure = new WebSocketException("receive failed") };
        var (_, receiver) = CreateReceiver(socket, maxIncomingMessageSize: 1024);

        await Assert.ThrowsAsync<WebSocketException>(() => receiver.RunAsync(CancellationToken.None));
        Assert.Equal(1, socket.ReceiveAttempts);
    }

    [Fact]
    public async Task RunAsync_WhenCancelled_CompletesNormally()
    {
        var socket = new ScriptedWebSocket();
        using var cancellation = new CancellationTokenSource();
        var (_, receiver) = CreateReceiver(socket, maxIncomingMessageSize: 1024);
        var loop = receiver.RunAsync(cancellation.Token);

        await cancellation.CancelAsync();
        await loop;
    }

    private static (ConnectionRegistry Registry, ConnectionReceiver Receiver) CreateReceiver(
        WebSocket socket,
        int maxIncomingMessageSize)
    {
        var registry = new ConnectionRegistry();
        registry.Add("connection-1", "user-1");
        var outgoing = new ConnectionBuffer(8, SlowClientPolicy.Disconnect);
        var processor = new ProtocolProcessor(registry, new AllowAllAuthorizer(), inboundHandler: null);
        return (
            registry,
            new ConnectionReceiver(
                socket,
                processor,
                "connection-1",
                "user-1",
                outgoing,
                maxIncomingMessageSize));
    }

    private sealed class AllowAllAuthorizer : ISubscriptionAuthorizer
    {
        public ValueTask<bool> AuthorizeAsync(
            SubscriptionAuthorizationContext context,
            CancellationToken cancellationToken) => ValueTask.FromResult(true);
    }

    private sealed class ScriptedWebSocket : WebSocket
    {
        private readonly Queue<ReceiveFrame> frames = [];
        private readonly TaskCompletionSource neverReceive = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Exception? ReceiveFailure { get; init; }

        public int ReceiveAttempts { get; private set; }

        public WebSocketCloseStatus? OutputCloseStatus { get; private set; }

        public string? OutputCloseDescription { get; private set; }

        public override WebSocketCloseStatus? CloseStatus => null;

        public override string? CloseStatusDescription => null;

        public override WebSocketState State => WebSocketState.Open;

        public override string? SubProtocol => null;

        public void AddText(string text, bool endOfMessage) =>
            frames.Enqueue(new ReceiveFrame(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, endOfMessage));

        public void AddClose(
            WebSocketCloseStatus status = WebSocketCloseStatus.NormalClosure,
            string? description = null) =>
            frames.Enqueue(new ReceiveFrame([], WebSocketMessageType.Close, true, status, description));

        public void AddBinary(byte[] payload) =>
            frames.Enqueue(new ReceiveFrame(payload, WebSocketMessageType.Binary, true));

        public override void Abort()
        {
        }

        public override Task CloseAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            OutputCloseStatus = closeStatus;
            OutputCloseDescription = statusDescription;
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            OutputCloseStatus = closeStatus;
            OutputCloseDescription = statusDescription;
            return Task.CompletedTask;
        }

        public override void Dispose()
        {
        }

        public override async Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken)
        {
            ReceiveAttempts++;
            if (ReceiveFailure is not null)
            {
                throw ReceiveFailure;
            }

            if (frames.TryDequeue(out var frame))
            {
                frame.Payload.AsSpan().CopyTo(buffer.AsSpan());
                return new WebSocketReceiveResult(
                    frame.Payload.Length,
                    frame.MessageType,
                    frame.EndOfMessage,
                    frame.CloseStatus,
                    frame.CloseDescription);
            }

            await neverReceive.Task.WaitAsync(cancellationToken);
            throw new InvalidOperationException("Unreachable wait completed.");
        }

        public override Task SendAsync(
            ArraySegment<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken) => Task.CompletedTask;

        private sealed record ReceiveFrame(
            byte[] Payload,
            WebSocketMessageType MessageType,
            bool EndOfMessage,
            WebSocketCloseStatus? CloseStatus = null,
            string? CloseDescription = null);
    }
}
