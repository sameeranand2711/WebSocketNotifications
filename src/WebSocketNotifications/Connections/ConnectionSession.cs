using System.Net.WebSockets;
using Microsoft.Extensions.Logging;
using WebSocketNotifications.Abstractions;
using WebSocketNotifications.Configuration;
using WebSocketNotifications.Protocol;

namespace WebSocketNotifications.Connections;

/// <summary>Coordinates the sender, receiver, and optional heartbeat loops for one connection.</summary>
internal sealed class ConnectionSession(
    ConnectionRegistry registry,
    ISubscriptionAuthorizer authorizer,
    IWebSocketInboundMessageHandler? inboundHandler,
    WebSocketNotificationOptions options,
    TimeProvider timeProvider,
    ILogger<ConnectionSession> logger)
{
    private static readonly Action<ILogger, string, string, Exception?> ConnectionOpened =
        LoggerMessage.Define<string, string>(
            LogLevel.Debug,
            new EventId(1, nameof(ConnectionOpened)),
            "WebSocket notification connection {ConnectionId} opened for user {UserId}");

    private static readonly Action<ILogger, string, string, Exception?> ConnectionClosed =
        LoggerMessage.Define<string, string>(
            LogLevel.Debug,
            new EventId(2, nameof(ConnectionClosed)),
            "WebSocket notification connection {ConnectionId} closed for user {UserId}");

    public async Task RunAsync(
        string connectionId,
        string userId,
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var outgoing = new ConnectionBuffer(options.OutgoingBufferCapacity, options.SlowClientPolicy);
        var heartbeatState = new HeartbeatState();
        registry.Add(connectionId, userId, outgoing, stop.Cancel);
        ConnectionOpened(logger, connectionId, userId, null);

        try
        {
            var processor = new ProtocolProcessor(registry, authorizer, inboundHandler, heartbeatState);
            var sender = new ConnectionSender(socket, outgoing);
            var receiver = new ConnectionReceiver(
                socket,
                processor,
                connectionId,
                userId,
                outgoing,
                options.MaxIncomingMessageSize);
            var tasks = new List<Task>
            {
                sender.RunAsync(stop.Token),
                receiver.RunAsync(stop.Token),
            };

            if (options.HeartbeatEnabled)
            {
                tasks.Add(
                    new HeartbeatLoop(
                            connectionId,
                            registry,
                            outgoing,
                            heartbeatState,
                            options.HeartbeatInterval,
                            options.HeartbeatTimeout,
                            timeProvider)
                        .RunAsync(stop.Token));
            }

            // Any loop ending means the connection can no longer be treated as healthy.
            // Cancel the siblings and await them so no socket task survives the session.
            await Task.WhenAny(tasks).ConfigureAwait(false);
            await stop.CancelAsync().ConfigureAwait(false);
            outgoing.Complete();
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(false);
            outgoing.Complete();
            registry.Remove(connectionId);
            ConnectionClosed(logger, connectionId, userId, null);
        }
    }
}
