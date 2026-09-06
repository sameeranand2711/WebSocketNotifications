using System.Net.WebSockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebSocketNotifications.Abstractions;
using WebSocketNotifications.Configuration;
using WebSocketNotifications.Connections;

namespace WebSocketNotifications.Hosting;

/// <summary>Maps the authenticated WebSocket notification endpoint.</summary>
public static class WebSocketNotificationEndpointRouteBuilderExtensions
{
    private static readonly Action<ILogger, string, Exception?> SocketFailure =
        LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(3, nameof(SocketFailure)),
            "WebSocket connection {ConnectionId} ended after a socket failure");

    /// <summary>Maps the configured WebSocket notification path and requires authorization.</summary>
    /// <param name="endpoints">The application's endpoint route builder.</param>
    /// <returns>A convention builder for the mapped endpoint.</returns>
    /// <remarks><c>UseWebSockets</c>, authentication, and authorization middleware must run before the endpoint.</remarks>
    public static IEndpointConventionBuilder MapWebSocketNotifications(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var options = endpoints.ServiceProvider
            .GetRequiredService<IOptions<WebSocketNotificationOptions>>()
            .Value;

        return endpoints.MapGet(options.EndpointPath, HandleAsync).RequireAuthorization();
    }

    private static async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var resolver = context.RequestServices.GetService<IWebSocketUserResolver>();
        if (resolver is null)
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            return;
        }

        var userId = await resolver.ResolveUserIdAsync(context, context.RequestAborted).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(userId))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var options = context.RequestServices
            .GetRequiredService<IOptions<WebSocketNotificationOptions>>()
            .Value;
        // Compression is opt-in because compressed attacker-controlled data can introduce
        // side-channel risk when it shares frames with application secrets.
        var acceptContext = new WebSocketAcceptContext
        {
            DangerousEnableCompression = options.CompressionEnabled,
        };

        using var socket = await context.WebSockets.AcceptWebSocketAsync(acceptContext).ConfigureAwait(false);
        var connectionId = Guid.NewGuid().ToString("N");
        try
        {
            await context.RequestServices
                .GetRequiredService<ConnectionSession>()
                .RunAsync(connectionId, userId, socket, context.RequestAborted)
                .ConfigureAwait(false);
        }
        catch (WebSocketException exception)
        {
            var logger = context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("WebSocketNotifications.Endpoint");
            SocketFailure(logger, connectionId, exception);
        }
    }
}
