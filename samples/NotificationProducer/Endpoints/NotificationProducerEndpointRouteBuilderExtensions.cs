using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NotificationProducer.Abstractions;
using NotificationProducer.Contracts;

namespace NotificationProducer.Endpoints;

/// <summary>Maps the sample's dimension-specific and combined notification endpoints.</summary>
internal static class NotificationProducerEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapNotificationProducerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup("/api/notifications")
            .WithTags("Notifications");
        group.MapPost("/users", (TargetedNotificationRequest request, INotificationPublisher publisher,
                TimeProvider timeProvider, CancellationToken cancellationToken) =>
            PublishTargetedAsync(request, RoutingDimension.User, publisher, timeProvider, cancellationToken))
            .WithSummary("Publish to one or more users")
            .Produces<PublishNotificationResponse>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem();
        group.MapPost("/groups", (TargetedNotificationRequest request, INotificationPublisher publisher,
                TimeProvider timeProvider, CancellationToken cancellationToken) =>
            PublishTargetedAsync(request, RoutingDimension.Group, publisher, timeProvider, cancellationToken))
            .WithSummary("Publish to one or more groups")
            .Produces<PublishNotificationResponse>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem();
        group.MapPost("/feeds", (TargetedNotificationRequest request, INotificationPublisher publisher,
                TimeProvider timeProvider, CancellationToken cancellationToken) =>
            PublishTargetedAsync(request, RoutingDimension.Feed, publisher, timeProvider, cancellationToken))
            .WithSummary("Publish to one or more feeds")
            .Produces<PublishNotificationResponse>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem();
        group.MapPost("/events", (TargetedNotificationRequest request, INotificationPublisher publisher,
                TimeProvider timeProvider, CancellationToken cancellationToken) =>
            PublishTargetedAsync(request, RoutingDimension.Event, publisher, timeProvider, cancellationToken))
            .WithSummary("Publish to one or more event types")
            .Produces<PublishNotificationResponse>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem();
        group.MapPost("", PublishCombinedAsync)
            .WithSummary("Publish to any combination of routing targets")
            .Produces<PublishNotificationResponse>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem();

        return endpoints;
    }

    private static Task<IResult> PublishTargetedAsync(
        TargetedNotificationRequest request,
        RoutingDimension dimension,
        INotificationPublisher publisher,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var requestTargets = request.Targets ?? [];
        var errors = Validate(requestTargets, request.Payload, request.ExpiresAt, request.Key, "targets");
        if (errors.Count > 0)
        {
            return Task.FromResult(Results.ValidationProblem(errors));
        }

        var targets = requestTargets.ToArray();
        var notification = CreateNotification(
            dimension == RoutingDimension.User ? targets : [],
            dimension == RoutingDimension.Group ? targets : [],
            dimension == RoutingDimension.Feed ? targets : [],
            dimension == RoutingDimension.Event ? targets : [],
            request.Payload,
            request.ExpiresAt,
            request.Key,
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
        var groups = request.Groups ?? [];
        var feeds = request.Feeds ?? [];
        var eventTypes = request.EventTypes ?? [];
        var allTargets = userIds.Concat(groups).Concat(feeds).Concat(eventTypes).ToArray();
        var errors = Validate(allTargets, request.Payload, request.ExpiresAt, request.Key, "routingTargets");
        if (errors.Count > 0)
        {
            return Task.FromResult(Results.ValidationProblem(errors));
        }

        var notification = CreateNotification(
            userIds,
            groups,
            feeds,
            eventTypes,
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
        string[] groups,
        string[] feeds,
        string[] eventTypes,
        JsonElement payload,
        DateTimeOffset? expiresAt,
        string? key,
        TimeProvider timeProvider)
    {
        // A stable key keeps related records on one Kafka partition. The caller may override
        // this default when its ordering domain spans several notification targets.
        var effectiveKey = key ?? DefaultKey(userIds, groups, feeds, eventTypes);
        return new QueuedNotification(
            Guid.NewGuid().ToString("N"),
            effectiveKey,
            userIds.ToArray(),
            groups.ToArray(),
            feeds.ToArray(),
            eventTypes.ToArray(),
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
        string[] groups,
        string[] feeds,
        string[] eventTypes)
    {
        if (userIds.FirstOrDefault() is { } userId)
        {
            return $"user:{userId}";
        }

        if (feeds.FirstOrDefault() is { } feed)
        {
            return $"feed:{feed}";
        }

        if (groups.FirstOrDefault() is { } group)
        {
            return $"group:{group}";
        }

        return $"event:{eventTypes[0]}";
    }

    private enum RoutingDimension
    {
        User,
        Group,
        Feed,
        Event,
    }
}
