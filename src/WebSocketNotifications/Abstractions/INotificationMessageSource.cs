using WebSocketNotifications.Contracts;

namespace WebSocketNotifications.Abstractions;

/// <summary>Provides neutral notifications from an application-owned queue or message source.</summary>
/// <remarks>
/// Implementations own transport concerns such as acknowledgement, retry, ordering, and checkpointing.
/// The library invokes at most one registered source and awaits the handler before requesting the next item.
/// </remarks>
public interface INotificationMessageSource
{
    /// <summary>Runs until cancellation and awaits the handler for each notification.</summary>
    /// <param name="handler">The callback that accepts a notification into local routing.</param>
    /// <param name="cancellationToken">Signals application shutdown.</param>
    /// <returns>A task that represents the lifetime of the source.</returns>
    Task RunAsync(
        Func<NotificationEnvelope, CancellationToken, Task> handler,
        CancellationToken cancellationToken);
}
