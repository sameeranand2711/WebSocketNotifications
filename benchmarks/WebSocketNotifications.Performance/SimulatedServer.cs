using System.Diagnostics.Metrics;
using System.Net.WebSockets;
using System.Text.Json;
using WebSocketNotifications.Configuration;
using WebSocketNotifications.Connections;
using WebSocketNotifications.Contracts;
using WebSocketNotifications.Delivery;
using WebSocketNotifications.Diagnostics;

namespace WebSocketNotifications.Performance;

internal sealed class SimulatedServer : IAsyncDisposable
{
    private const string SharedSubscription = "load:shared";
    private readonly int serverIndex;
    private readonly PerformanceOptions options;
    private readonly Meter meter;
    private readonly WebSocketNotificationMetrics metrics;
    private readonly ConnectionRegistry registry;
    private readonly NotificationDispatcher dispatcher;
    private readonly List<SimulatedConnection> connections = [];
    private bool stopped;
    private int generation;
    private int churnOffset;

    private SimulatedServer(int serverIndex, PerformanceOptions options)
    {
        this.serverIndex = serverIndex;
        this.options = options;
        meter = new Meter(WebSocketNotificationMetrics.MeterName);
        metrics = new WebSocketNotificationMetrics(meter);
        registry = new ConnectionRegistry(
            metrics,
            WebSocketNotificationOptions.DefaultMaxSubscriptionsPerConnection,
            WebSocketNotificationOptions.DefaultMaxSubscriptionKeyLength);
        dispatcher = new NotificationDispatcher(
            registry,
            new NotificationRouter(registry),
            256 * 1024,
            metrics);
    }

    public static SimulatedServer Start(int serverIndex, PerformanceOptions options)
    {
        var server = new SimulatedServer(serverIndex, options);
        for (var slot = 0; slot < options.ConnectionsPerServer; slot++)
        {
            server.connections.Add(server.StartConnection(slot));
        }

        return server;
    }

    public ValueTask PublishAsync(NotificationEnvelope notification, CancellationToken cancellationToken) =>
        dispatcher.DispatchAsync(notification, DateTimeOffset.UtcNow, cancellationToken);

    public async Task ChurnAsync(CancellationToken cancellationToken)
    {
        var churnCount = Math.Max(1, options.ConnectionsPerServer * options.ChurnPercent / 100);
        for (var index = 0; index < churnCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var slot = (churnOffset + index) % connections.Count;
            await connections[slot].StopAsync(drain: false).ConfigureAwait(false);
            connections[slot] = StartConnection(slot);
        }

        churnOffset = (churnOffset + churnCount) % connections.Count;
    }

    public async Task StopAsync()
    {
        if (stopped)
        {
            return;
        }

        stopped = true;
        foreach (var connection in connections)
        {
            await connection.StopAsync(drain: true).ConfigureAwait(false);
        }

        connections.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        meter.Dispose();
    }

    private SimulatedConnection StartConnection(int slot)
    {
        var connectionId = $"server-{serverIndex}-generation-{generation++}-slot-{slot}";
        var buffer = new ConnectionBuffer(options.BufferCapacity, SlowClientPolicy.DropCurrent, metrics);
        var cancellation = new CancellationTokenSource();
        var socket = new SimulatedWebSocket(
            IsSlow(slot) ? TimeSpan.FromMilliseconds(options.SlowSendDelayMilliseconds) : TimeSpan.Zero);
        registry.Add(connectionId, $"user-{serverIndex}-{slot}", buffer, cancellation.Cancel);
        var subscriptions = Enumerable.Range(0, options.SubscriptionsPerConnection)
            .Select(index => index == 0 ? SharedSubscription : $"load:{slot}:{index}")
            .ToArray();
        var result = registry.AddSubscriptions(connectionId, subscriptions);
        if (result is not SubscriptionAddResult.Added)
        {
            throw new InvalidOperationException($"Could not initialize subscriptions for {connectionId}: {result}.");
        }

        var sender = new ConnectionSender(socket, buffer, metrics).RunAsync(cancellation.Token);
        return new SimulatedConnection(connectionId, registry, buffer, cancellation, sender);
    }

    private bool IsSlow(int slot) => slot < options.ConnectionsPerServer * options.SlowClientPercent / 100;

    internal static NotificationEnvelope CreateNotification(long sequence, int payloadBytes)
    {
        var payload = JsonSerializer.SerializeToElement(new
        {
            sequence,
            data = new string('x', Math.Max(1, payloadBytes - 64)),
        });
        return new NotificationEnvelope(
            $"load-{sequence}",
            payload,
            DateTimeOffset.UtcNow,
            expiresAt: null,
            userIds: null,
            subscriptions: [SharedSubscription]);
    }

    private sealed class SimulatedConnection(
        string connectionId,
        ConnectionRegistry registry,
        ConnectionBuffer buffer,
        CancellationTokenSource cancellation,
        Task sender)
    {
        public async Task StopAsync(bool drain)
        {
            registry.Remove(connectionId);
            buffer.Complete();
            if (!drain)
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
            }

            try
            {
                await sender.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }

            buffer.DiscardPending();
            cancellation.Dispose();
        }
    }

    private sealed class SimulatedWebSocket(TimeSpan sendDelay) : WebSocket
    {
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => WebSocketState.Open;
        public override string? SubProtocol => null;
        public override void Abort() { }
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;
        public override void Dispose() { }
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken) => throw new NotSupportedException();

        public override async Task SendAsync(
            ArraySegment<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken)
        {
            if (sendDelay > TimeSpan.Zero)
            {
                await Task.Delay(sendDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
