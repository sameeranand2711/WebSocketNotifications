using System.Diagnostics.Metrics;

namespace WebSocketNotifications.Diagnostics;

/// <summary>Records low-cardinality operational measurements for the notification runtime.</summary>
internal sealed class WebSocketNotificationMetrics
{
    internal const string MeterName = "WebSocketNotifications";

    private long activeConnectionCount;
    private long activeSubscriptionCount;
    private long queuedMessageCount;
    private readonly bool enabled;
    private readonly Counter<long>? notificationsReceived;
    private readonly Counter<long>? notificationsExpired;
    private readonly Counter<long>? notificationsMatched;
    private readonly Counter<long>? notificationsUnmatched;
    private readonly Counter<long>? messagesEnqueued;
    private readonly Counter<long>? messagesDropped;
    private readonly Counter<long>? oversizedMessagesRejected;
    private readonly Counter<long>? slowClientDrops;
    private readonly Counter<long>? slowClientDisconnects;
    private readonly Counter<long>? messagesSent;
    private readonly Counter<long>? messageSendFailures;
    private readonly Counter<long>? acceptedSubscriptionCommands;
    private readonly Counter<long>? deniedSubscriptionCommands;
    private readonly Counter<long>? invalidSubscriptionCommands;
    private readonly Counter<long>? overLimitSubscriptionCommands;

    private WebSocketNotificationMetrics()
    {
    }

    internal WebSocketNotificationMetrics(IMeterFactory meterFactory)
        : this(meterFactory.Create(MeterName))
    {
    }

    internal WebSocketNotificationMetrics(Meter meter)
    {
        enabled = true;
        _ = meter.CreateObservableGauge(
            "websocket_notifications.connections.active",
            () => Interlocked.Read(ref activeConnectionCount),
            unit: "{connection}",
            description: "Current WebSocket notification connections.");
        _ = meter.CreateObservableGauge(
            "websocket_notifications.subscriptions.active",
            () => Interlocked.Read(ref activeSubscriptionCount),
            unit: "{subscription}",
            description: "Current connection-to-subscription associations.");
        _ = meter.CreateObservableGauge(
            "websocket_notifications.messages.queued",
            () => Interlocked.Read(ref queuedMessageCount),
            unit: "{message}",
            description: "Current messages waiting in WebSocket connection buffers.");
        notificationsReceived = meter.CreateCounter<long>("websocket_notifications.notifications.received");
        notificationsExpired = meter.CreateCounter<long>("websocket_notifications.notifications.expired");
        notificationsMatched = meter.CreateCounter<long>("websocket_notifications.notifications.matched");
        notificationsUnmatched = meter.CreateCounter<long>("websocket_notifications.notifications.unmatched");
        messagesEnqueued = meter.CreateCounter<long>("websocket_notifications.messages.enqueued");
        messagesDropped = meter.CreateCounter<long>("websocket_notifications.messages.dropped");
        oversizedMessagesRejected = meter.CreateCounter<long>(
            "websocket_notifications.messages.rejected_oversized");
        slowClientDrops = meter.CreateCounter<long>("websocket_notifications.clients.slow_drops");
        slowClientDisconnects = meter.CreateCounter<long>("websocket_notifications.clients.slow_disconnects");
        messagesSent = meter.CreateCounter<long>("websocket_notifications.messages.sent");
        messageSendFailures = meter.CreateCounter<long>("websocket_notifications.messages.send_failures");
        acceptedSubscriptionCommands = meter.CreateCounter<long>(
            "websocket_notifications.subscriptions.commands.accepted");
        deniedSubscriptionCommands = meter.CreateCounter<long>(
            "websocket_notifications.subscriptions.commands.denied");
        invalidSubscriptionCommands = meter.CreateCounter<long>(
            "websocket_notifications.subscriptions.commands.invalid");
        overLimitSubscriptionCommands = meter.CreateCounter<long>(
            "websocket_notifications.subscriptions.commands.over_limit");
    }

    internal static WebSocketNotificationMetrics Disabled { get; } = new();

    internal void ConnectionOpened()
    {
        if (enabled)
        {
            Interlocked.Increment(ref activeConnectionCount);
        }
    }

    internal void ConnectionClosed()
    {
        if (enabled)
        {
            Interlocked.Decrement(ref activeConnectionCount);
        }
    }

    internal void SubscriptionsAdded(int count)
    {
        if (enabled)
        {
            Interlocked.Add(ref activeSubscriptionCount, count);
        }
    }

    internal void SubscriptionsRemoved(int count)
    {
        if (enabled)
        {
            Interlocked.Add(ref activeSubscriptionCount, -count);
        }
    }

    internal void NotificationReceived() => notificationsReceived?.Add(1);

    internal void NotificationExpired() => notificationsExpired?.Add(1);

    internal void NotificationMatched() => notificationsMatched?.Add(1);

    internal void NotificationUnmatched() => notificationsUnmatched?.Add(1);

    internal void MessageEnqueued()
    {
        if (enabled)
        {
            Interlocked.Increment(ref queuedMessageCount);
        }

        messagesEnqueued?.Add(1);
    }

    internal void MessageDequeued()
    {
        if (enabled)
        {
            Interlocked.Decrement(ref queuedMessageCount);
        }
    }

    internal void QueuedMessageDropped()
    {
        MessageDequeued();
        SlowClientMessageDropped();
    }

    internal void SlowClientMessageDropped()
    {
        messagesDropped?.Add(1);
        slowClientDrops?.Add(1);
    }

    internal void SlowClientDisconnected()
    {
        messagesDropped?.Add(1);
        slowClientDisconnects?.Add(1);
    }

    internal void OversizedMessageRejected() => oversizedMessagesRejected?.Add(1);

    internal void MessageSent() => messagesSent?.Add(1);

    internal void MessageSendFailed() => messageSendFailures?.Add(1);

    internal void SubscriptionCommandAccepted() => acceptedSubscriptionCommands?.Add(1);

    internal void SubscriptionCommandDenied() => deniedSubscriptionCommands?.Add(1);

    internal void SubscriptionCommandInvalid() => invalidSubscriptionCommands?.Add(1);

    internal void SubscriptionCommandOverLimit() => overLimitSubscriptionCommands?.Add(1);
}
