using Microsoft.AspNetCore.Http;

namespace WebSocketNotifications.Abstractions;

/// <summary>Resolves the direct-routing user identifier from an authenticated request.</summary>
public interface IWebSocketUserResolver
{
    /// <summary>Returns the application user identifier, or <see langword="null"/> when unavailable.</summary>
    /// <param name="context">The authenticated HTTP context for the WebSocket upgrade request.</param>
    /// <param name="cancellationToken">Signals that the request was aborted.</param>
    /// <returns>The stable application user identifier used for direct routing.</returns>
    ValueTask<string?> ResolveUserIdAsync(HttpContext context, CancellationToken cancellationToken);
}
