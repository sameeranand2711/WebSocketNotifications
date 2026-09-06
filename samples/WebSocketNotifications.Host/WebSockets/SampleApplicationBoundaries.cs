using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using WebSocketNotifications.Abstractions;

namespace WebSocketNotifications.Host.WebSockets;

/// <summary>Maps the authenticated name-identifier claim to the library's direct-routing identity.</summary>
internal sealed class ClaimUserResolver : IWebSocketUserResolver
{
    public ValueTask<string?> ResolveUserIdAsync(
        HttpContext context,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(context.User.FindFirstValue(ClaimTypes.NameIdentifier));
}

/// <summary>Demonstrates the application-owned subscription authorization boundary.</summary>
internal sealed class SampleSubscriptionAuthorizer : ISubscriptionAuthorizer
{
    // The sample permits every subscription to make each routing mode easy to exercise.
    // Real applications should check membership or ACLs using context.UserId.
    public ValueTask<bool> AuthorizeAsync(
        SubscriptionAuthorizationContext context,
        CancellationToken cancellationToken) => ValueTask.FromResult(true);
}

/// <summary>Demonstrates handling application messages that are not reserved by the protocol.</summary>
internal sealed class SampleInboundMessageHandler(ILogger<SampleInboundMessageHandler> logger)
    : IWebSocketInboundMessageHandler
{
    private static readonly Action<ILogger, string, string, Exception?> MessageReceived =
        LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(100, nameof(MessageReceived)),
            "Application message received from user {UserId} on connection {ConnectionId}");

    public ValueTask HandleAsync(
        InboundWebSocketMessage message,
        CancellationToken cancellationToken)
    {
        MessageReceived(logger, message.UserId, message.ConnectionId, null);
        return ValueTask.CompletedTask;
    }
}
