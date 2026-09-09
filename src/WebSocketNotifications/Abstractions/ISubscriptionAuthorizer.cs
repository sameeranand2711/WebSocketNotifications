namespace WebSocketNotifications.Abstractions;

/// <summary>Allows an application to authorize opaque, application-defined subscription keys.</summary>
/// <remarks>Direct user routing is derived from authentication and is not authorized through this interface.</remarks>
public interface ISubscriptionAuthorizer
{
    /// <summary>Determines whether the requested subscription is allowed.</summary>
    /// <param name="context">The authenticated connection and requested subscription.</param>
    /// <param name="cancellationToken">Signals that the connection is closing.</param>
    /// <returns><see langword="true"/> when the subscription may be added; otherwise <see langword="false"/>.</returns>
    ValueTask<bool> AuthorizeAsync(
        SubscriptionAuthorizationContext context,
        CancellationToken cancellationToken);
}

/// <summary>Describes the authenticated connection and requested subscription.</summary>
/// <param name="ConnectionId">The library-assigned identifier for this socket connection.</param>
/// <param name="UserId">The application user identifier resolved from the authenticated request.</param>
/// <param name="Subscription">The opaque subscription key being requested.</param>
public sealed record SubscriptionAuthorizationContext(
    string ConnectionId,
    string UserId,
    string Subscription);
