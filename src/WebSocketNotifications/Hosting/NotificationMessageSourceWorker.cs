using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WebSocketNotifications.Abstractions;
using WebSocketNotifications.Delivery;

namespace WebSocketNotifications.Hosting;

/// <summary>Bridges the optional application message source into the in-memory notification hub.</summary>
internal sealed class NotificationMessageSourceWorker(
    IEnumerable<INotificationMessageSource> sources,
    Lazy<WebSocketNotificationHub> hub,
    ILogger<NotificationMessageSourceWorker> logger) : BackgroundService
{
    private static readonly Action<ILogger, string, Exception?> SourceStarting =
        LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(4, nameof(SourceStarting)),
            "Starting WebSocket notification message source {MessageSourceType}");

    internal Task RunSourceAsync(CancellationToken cancellationToken)
    {
        // Enumerating at most two is enough to validate the one-source contract without
        // needlessly realizing an arbitrary application-owned sequence.
        var configuredSources = sources.Take(2).ToArray();
        if (configuredSources.Length == 0)
        {
            return Task.CompletedTask;
        }

        if (configuredSources.Length > 1)
        {
            throw new InvalidOperationException(
                "Only one INotificationMessageSource may be registered because source ordering semantics are provider-owned.");
        }

        var source = configuredSources[0];
        SourceStarting(logger, source.GetType().FullName ?? source.GetType().Name, null);
        // Resolve the hub only when a source exists. This keeps direct-publish-only apps from
        // constructing the routing graph solely because the hosted service starts.
        var notificationHub = hub.Value;
        return source.RunAsync(
            (notification, handlerCancellation) =>
                notificationHub.PublishAsync(notification, handlerCancellation).AsTask(),
            cancellationToken);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => RunSourceAsync(stoppingToken);
}
