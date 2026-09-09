using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NotificationProducer.Abstractions;
using NotificationProducer.Contracts;

namespace NotificationProducer.Endpoints;

/// <summary>Maps the sample's direct-user, subscription, and combined notification endpoints.</summary>
internal static class NotificationProducerEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapNotificationProducerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup("/api/notifications")
            .WithTags("Notifications");
        group.MapPost("/users", (UserNotificationRequest request, INotificationPublisher publisher,
                TimeProvider timeProvider, CancellationToken cancellationToken) =>
            PublishTargetedAsync(
                request.Users,
                request.Payload,
                request.ExpiresAt,
                request.Key,
                targetsAreUsers: true,
                "users",
                publisher,
                timeProvider,
                cancellationToken))
            .WithSummary("Publish to one or more users")
            .Produces<PublishNotificationResponse>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem();
        group.MapPost("/subscriptions", (SubscriptionNotificationRequest request, INotificationPublisher publisher,
                TimeProvider timeProvider, CancellationToken cancellationToken) =>
            PublishTargetedAsync(
                request.Subscriptions,
                request.Payload,
                request.ExpiresAt,
                request.Key,
                targetsAreUsers: false,
                "subscriptions",
                publisher,
                timeProvider,
                cancellationToken))
            .WithSummary("Publish to one or more opaque subscription keys")
            .Produces<PublishNotificationResponse>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem();
        group.MapPost("", PublishCombinedAsync)
            .WithSummary("Publish to any combination of routing targets")
            .Produces<PublishNotificationResponse>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem();

        return endpoints;
    }

    private static Task<IResult> PublishTargetedAsync(
        string[]? requestedTargets,
        JsonElement payload,
        DateTimeOffset? expiresAt,
        string? key,
        bool targetsAreUsers,
        string targetProperty,
        INotificationPublisher publisher,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var requestTargets = requestedTargets ?? [];
        var errors = Validate(requestTargets, payload, expiresAt, key, targetProperty);
        if (errors.Count > 0)
        {
            return Task.FromResult(Results.ValidationProblem(errors));
        }

        var targets = requestTargets.ToArray();
        var notification = CreateNotification(
            targetsAreUsers ? targets : [],
            targetsAreUsers ? [] : targets,
            payload,
            expiresAt,
            key,
            timeProvider);
        return PublishAsync(notification, publisher, cancellationToken);
    }

    private static Task<IResult> PublishCombinedAsync(
        CombinedNotificationRequest request,
        INotificationPublisher publisher,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var userIds = request.UserIds ?? [];
        var subscriptions = request.Subscriptions ?? [];
        var allTargets = userIds.Concat(subscriptions).ToArray();
        var errors = Validate(allTargets, request.Payload, request.ExpiresAt, request.Key, "routingTargets");
        if (errors.Count > 0)
        {
            return Task.FromResult(Results.ValidationProblem(errors));
        }

        var notification = CreateNotification(
            userIds,
            subscriptions,
            request.Payload,
            request.ExpiresAt,
            request.Key,
            timeProvider);
        return PublishAsync(notification, publisher, cancellationToken);
    }

    private static async Task<IResult> PublishAsync(
        QueuedNotification notification,
        INotificationPublisher publisher,
        CancellationToken cancellationToken)
    {
        var receipt = await publisher.PublishAsync(notification, cancellationToken);
        return Results.Accepted(
            value: new PublishNotificationResponse(
                notification.MessageId,
                notification.Key,
                receipt.Topic,
                receipt.Partition,
                receipt.Offset));
    }

    private static QueuedNotification CreateNotification(
        string[] userIds,
        string[] subscriptions,
        JsonElement payload,
        DateTimeOffset? expiresAt,
        string? key,
        TimeProvider timeProvider)
    {
        // A stable key keeps related records on one Kafka partition. The caller may override
        // this default when its ordering domain spans several notification targets.
        var effectiveKey = key ?? DefaultKey(userIds, subscriptions);
        return new QueuedNotification(
            Guid.NewGuid().ToString("N"),
            effectiveKey,
            userIds.ToArray(),
            subscriptions.ToArray(),
            payload.Clone(),
            timeProvider.GetUtcNow(),
            expiresAt);
    }

    private static Dictionary<string, string[]> Validate(
        string[] targets,
        JsonElement payload,
        DateTimeOffset? expiresAt,
        string? key,
        string targetProperty)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (targets.Length == 0 || targets.Any(string.IsNullOrWhiteSpace))
        {
            errors[targetProperty] = ["At least one nonblank routing target is required."];
        }

        if (payload.ValueKind == JsonValueKind.Undefined)
        {
            errors["payload"] = ["A JSON payload is required."];
        }

        if (expiresAt is { } expiry && expiry.Offset != TimeSpan.Zero)
        {
            errors["expiresAt"] = ["ExpiresAt must use the UTC offset."];
        }

        if (key is not null && string.IsNullOrWhiteSpace(key))
        {
            errors["key"] = ["Key must be nonblank when supplied."];
        }

        return errors;
    }

    private static string DefaultKey(
        string[] userIds,
        string[] subscriptions)
    {
        if (userIds.FirstOrDefault() is { } userId)
        {
            return $"user:{userId}";
        }

        return subscriptions[0];
    }
}
