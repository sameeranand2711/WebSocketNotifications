using System.Diagnostics.Metrics;
using System.Net.WebSockets;
using System.Text.Json;
using WebSocketNotifications.Diagnostics;
using Xunit;

namespace WebSocketNotifications.Tests.Diagnostics;

public sealed class ConnectionMetricsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 1, 2, 3, TimeSpan.Zero);

    [Fact]
    public void Registry_ReportsActiveConnectionsAndSubscriptionsThroughCleanup()
    {
        using var meter = new Meter($"WebSocketNotifications.Tests.{Guid.NewGuid():N}");
        using var measurements = new MetricMeasurements(meter);
        var metrics = new WebSocketNotificationMetrics(meter);
        var registry = new ConnectionRegistry(metrics, maxSubscriptionsPerConnection: 4, maxSubscriptionKeyLength: 32);

        registry.Add("connection-1", "user-1");
        registry.Add("connection-2", "user-1");
        registry.AddSubscriptions("connection-1", ["first", "second"]);
        registry.AddSubscription("connection-2", "second");

        Assert.Equal(2, measurements.Sum("websocket_notifications.connections.active"));
        Assert.Equal(3, measurements.Sum("websocket_notifications.subscriptions.active"));

        registry.RemoveSubscription("connection-1", "first");
        registry.Remove("connection-1");
        registry.Remove("connection-2");

        Assert.Equal(0, measurements.Sum("websocket_notifications.connections.active"));
        Assert.Equal(0, measurements.Sum("websocket_notifications.subscriptions.active"));
        Assert.All(measurements.RecordedTags, tags => Assert.Empty(tags));
    }

    [Fact]
    public async Task Dispatcher_SeparatesRoutingEnqueueDropsAndOutgoingSizeRejection()
    {
        using var meter = new Meter($"WebSocketNotifications.Tests.{Guid.NewGuid():N}");
        using var measurements = new MetricMeasurements(meter);
        var metrics = new WebSocketNotificationMetrics(meter);
        var registry = new ConnectionRegistry(metrics, 4, 32);
        var buffer = new ConnectionBuffer(1, SlowClientPolicy.DropCurrent, metrics);
        registry.Add("connection-1", "user-1", buffer, requestStop: null);
        var dispatcher = new NotificationDispatcher(registry, new NotificationRouter(registry), 256, metrics);

        await dispatcher.DispatchAsync(Notification("matched-1", ["user-1"], new { value = 1 }), Now);
        await dispatcher.DispatchAsync(Notification("unmatched", ["missing"], new { value = 2 }), Now);
        await dispatcher.DispatchAsync(
            Notification("expired", ["user-1"], new { value = 3 }, expiresAt: Now),
            Now);
        await dispatcher.DispatchAsync(Notification("matched-dropped", ["user-1"], new { value = 4 }), Now);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dispatcher.DispatchAsync(
                Notification("oversized", ["user-1"], new { value = new string('x', 512) }),
                Now).AsTask());

        Assert.Equal(5, measurements.Sum("websocket_notifications.notifications.received"));
        Assert.Equal(1, measurements.Sum("websocket_notifications.notifications.expired"));
        Assert.Equal(3, measurements.Sum("websocket_notifications.notifications.matched"));
        Assert.Equal(1, measurements.Sum("websocket_notifications.notifications.unmatched"));
        Assert.Equal(1, measurements.Sum("websocket_notifications.messages.enqueued"));
        Assert.Equal(1, measurements.Sum("websocket_notifications.messages.dropped"));
        Assert.Equal(1, measurements.Sum("websocket_notifications.messages.rejected_oversized"));
        Assert.Equal(1, measurements.Sum("websocket_notifications.clients.slow_drops"));
        Assert.All(measurements.RecordedTags, tags => Assert.Empty(tags));
    }

    [Fact]
    public async Task Sender_SeparatesEnqueueAcceptanceFromSendCompletionAndFailure()
    {
        using var meter = new Meter($"WebSocketNotifications.Tests.{Guid.NewGuid():N}");
        using var measurements = new MetricMeasurements(meter);
        var metrics = new WebSocketNotificationMetrics(meter);
        var successfulBuffer = new ConnectionBuffer(1, SlowClientPolicy.Disconnect, metrics);
        successfulBuffer.TryEnqueue(new byte[] { 1, 2, 3 });
        successfulBuffer.Complete();

        await new ConnectionSender(new SendWebSocket(), successfulBuffer, metrics)
            .RunAsync(CancellationToken.None);

        var failedBuffer = new ConnectionBuffer(1, SlowClientPolicy.Disconnect, metrics);
        failedBuffer.TryEnqueue(new byte[] { 4, 5, 6 });
        failedBuffer.Complete();
        await Assert.ThrowsAsync<WebSocketException>(() =>
            new ConnectionSender(new SendWebSocket(fail: true), failedBuffer, metrics)
                .RunAsync(CancellationToken.None));

        Assert.Equal(2, measurements.Sum("websocket_notifications.messages.enqueued"));
        Assert.Equal(1, measurements.Sum("websocket_notifications.messages.sent"));
        Assert.Equal(1, measurements.Sum("websocket_notifications.messages.send_failures"));
        Assert.All(measurements.RecordedTags, tags => Assert.Empty(tags));
    }

    [Fact]
    public void Buffer_ReportsDropOldestAndDisconnectPolicies()
    {
        using var meter = new Meter($"WebSocketNotifications.Tests.{Guid.NewGuid():N}");
        using var measurements = new MetricMeasurements(meter);
        var metrics = new WebSocketNotificationMetrics(meter);
        var dropOldest = new ConnectionBuffer(1, SlowClientPolicy.DropOldest, metrics);
        var disconnect = new ConnectionBuffer(1, SlowClientPolicy.Disconnect, metrics);

        Assert.Equal(BufferWriteResult.Enqueued, dropOldest.TryEnqueue(new byte[] { 1 }));
        Assert.Equal(BufferWriteResult.Enqueued, dropOldest.TryEnqueue(new byte[] { 2 }));
        Assert.Equal(BufferWriteResult.Enqueued, disconnect.TryEnqueue(new byte[] { 3 }));
        Assert.Equal(BufferWriteResult.Disconnect, disconnect.TryEnqueue(new byte[] { 4 }));

        Assert.Equal(3, measurements.Sum("websocket_notifications.messages.enqueued"));
        Assert.Equal(2, measurements.Sum("websocket_notifications.messages.dropped"));
        Assert.Equal(1, measurements.Sum("websocket_notifications.clients.slow_drops"));
        Assert.Equal(1, measurements.Sum("websocket_notifications.clients.slow_disconnects"));
        Assert.All(measurements.RecordedTags, tags => Assert.Empty(tags));
    }

    [Fact]
    public async Task Protocol_ReportsAcceptedDeniedInvalidAndOverLimitCommands()
    {
        using var meter = new Meter($"WebSocketNotifications.Tests.{Guid.NewGuid():N}");
        using var measurements = new MetricMeasurements(meter);
        var metrics = new WebSocketNotificationMetrics(meter);
        var registry = new ConnectionRegistry(metrics, 1, 8);
        registry.Add("connection-1", "user-1");
        var outgoing = new ConnectionBuffer(8, SlowClientPolicy.Disconnect, metrics);
        var allowed = new ProtocolProcessor(registry, new FixedAuthorizer(true), null, null, metrics);

        await allowed.ProcessAsync("connection-1", "user-1", Json("""{"type":"subscribe","subscriptions":["first"]}"""), outgoing, CancellationToken.None);
        await allowed.ProcessAsync("connection-1", "user-1", Json("""{"type":"unsubscribe","subscriptions":["first"]}"""), outgoing, CancellationToken.None);
        await allowed.ProcessAsync("connection-1", "user-1", Json("""{"type":"subscribe"}"""), outgoing, CancellationToken.None);
        await allowed.ProcessAsync("connection-1", "user-1", Json("""{"type":"subscribe","subscriptions":["123456789"]}"""), outgoing, CancellationToken.None);
        var denied = new ProtocolProcessor(registry, new FixedAuthorizer(false), null, null, metrics);
        await denied.ProcessAsync("connection-1", "user-1", Json("""{"type":"subscribe","subscriptions":["denied"]}"""), outgoing, CancellationToken.None);

        Assert.Equal(2, measurements.Sum("websocket_notifications.subscriptions.commands.accepted"));
        Assert.Equal(1, measurements.Sum("websocket_notifications.subscriptions.commands.denied"));
        Assert.Equal(1, measurements.Sum("websocket_notifications.subscriptions.commands.invalid"));
        Assert.Equal(1, measurements.Sum("websocket_notifications.subscriptions.commands.over_limit"));
        Assert.All(measurements.RecordedTags, tags => Assert.Empty(tags));
    }

    [Fact]
    public async Task Receiver_ReportsIncomingOversizeRejection()
    {
        using var meter = new Meter($"WebSocketNotifications.Tests.{Guid.NewGuid():N}");
        using var measurements = new MetricMeasurements(meter);
        var metrics = new WebSocketNotificationMetrics(meter);
        var registry = new ConnectionRegistry(metrics, 4, 32);
        registry.Add("connection-1", "user-1");
        var outgoing = new ConnectionBuffer(2, SlowClientPolicy.Disconnect, metrics);
        var processor = new ProtocolProcessor(registry, new FixedAuthorizer(true), null, null, metrics);
        var receiver = new ConnectionReceiver(
            new OversizedReceiveWebSocket(),
            processor,
            "connection-1",
            "user-1",
            outgoing,
            maxIncomingMessageSize: 32,
            metrics: metrics);

        await receiver.RunAsync(CancellationToken.None);

        Assert.Equal(1, measurements.Sum("websocket_notifications.messages.rejected_oversized"));
    }

    private static NotificationEnvelope Notification(
        string messageId,
        IEnumerable<string> users,
        object payload,
        DateTimeOffset? expiresAt = null) =>
        new(
            messageId,
            JsonSerializer.SerializeToElement(payload),
            Now.AddMinutes(-1),
            expiresAt,
            users);

    private static ReadOnlyMemory<byte> Json(string value) => System.Text.Encoding.UTF8.GetBytes(value);

    private sealed class MetricMeasurements : IDisposable
    {
        private readonly object gate = new();
        private readonly Dictionary<string, long> totals = new(StringComparer.Ordinal);
        private readonly MeterListener listener = new();

        public MetricMeasurements(Meter meter)
        {
            listener.InstrumentPublished = (instrument, currentListener) =>
            {
                if (ReferenceEquals(instrument.Meter, meter))
                {
                    currentListener.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>(Record);
            listener.Start();
        }

        public List<KeyValuePair<string, object?>[]> RecordedTags { get; } = [];

        public long Sum(string instrumentName)
        {
            listener.RecordObservableInstruments();
            lock (gate)
            {
                return totals.GetValueOrDefault(instrumentName);
            }
        }

        public void Dispose() => listener.Dispose();

        private void Record(
            Instrument instrument,
            long measurement,
            ReadOnlySpan<KeyValuePair<string, object?>> tags,
            object? state)
        {
            lock (gate)
            {
                totals[instrument.Name] = instrument is ObservableGauge<long>
                    ? measurement
                    : totals.GetValueOrDefault(instrument.Name) + measurement;
                RecordedTags.Add(tags.ToArray());
            }
        }
    }

    private sealed class SendWebSocket(bool fail = false) : WebSocket
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
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) =>
            fail ? Task.FromException(new WebSocketException("send failed")) : Task.CompletedTask;
    }

    private sealed class FixedAuthorizer(bool allowed) : ISubscriptionAuthorizer
    {
        public ValueTask<bool> AuthorizeAsync(
            SubscriptionAuthorizationContext context,
            CancellationToken cancellationToken) => ValueTask.FromResult(allowed);
    }

    private sealed class OversizedReceiveWebSocket : WebSocket
    {
        private bool received;

        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => WebSocketState.Open;
        public override string? SubProtocol => null;
        public override void Abort() { }
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;
        public override void Dispose() { }

        public override Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken)
        {
            Assert.False(received);
            received = true;
            new byte[33].CopyTo(buffer.Array!, buffer.Offset);
            return Task.FromResult(new WebSocketReceiveResult(33, WebSocketMessageType.Text, true));
        }

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
