using System.Text.Json;

namespace WebSocketNotifications.Abstractions;

/// <summary>Receives application-specific inbound JSON messages.</summary>
/// <remarks>Protocol-reserved subscribe, unsubscribe, and pong messages are handled by the library.</remarks>
public interface IWebSocketInboundMessageHandler
{
    /// <summary>Handles one application-specific message.</summary>
    /// <param name="message">The authenticated connection metadata and cloned JSON payload.</param>
    /// <param name="cancellationToken">Signals that the connection is closing.</param>
    /// <returns>A value task that completes when application processing has finished.</returns>
    ValueTask HandleAsync(InboundWebSocketMessage message, CancellationToken cancellationToken);
}

/// <summary>An application-specific message from an authenticated connection.</summary>
/// <param name="ConnectionId">The library-assigned socket connection identifier.</param>
/// <param name="UserId">The resolved application user identifier.</param>
/// <param name="Payload">A cloned JSON object whose lifetime is independent of the receive buffer.</param>
public sealed record InboundWebSocketMessage(string ConnectionId, string UserId, JsonElement Payload);
