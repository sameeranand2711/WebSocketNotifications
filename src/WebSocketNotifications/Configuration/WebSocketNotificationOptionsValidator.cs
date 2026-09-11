using Microsoft.Extensions.Options;

namespace WebSocketNotifications.Configuration;

internal sealed class WebSocketNotificationOptionsValidator : IValidateOptions<WebSocketNotificationOptions>
{
    internal const int AbsoluteMaxIncomingMessageSize = 1024 * 1024;
    internal const int AbsoluteMaxOutgoingMessageSize = 4 * 1024 * 1024;
    internal const int AbsoluteMaxOutgoingBufferCapacity = 10_000;
    internal const int AbsoluteMaxSubscriptionsPerConnection = 10_000;
    internal const int AbsoluteMaxSubscriptionKeyLength = 4_096;

    private static readonly TimeSpan MaximumHeartbeatDuration = TimeSpan.FromHours(1);

    public ValidateOptionsResult Validate(string? name, WebSocketNotificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.EndpointPath) ||
            options.EndpointPath[0] != '/' ||
            options.EndpointPath.Contains('?', StringComparison.Ordinal) ||
            options.EndpointPath.Contains('#', StringComparison.Ordinal))
        {
            failures.Add($"{nameof(options.EndpointPath)} must be an absolute path without a query or fragment.");
        }

        AddDurationFailureIfInvalid(
            failures,
            nameof(options.HeartbeatInterval),
            options.HeartbeatInterval);
        AddDurationFailureIfInvalid(
            failures,
            nameof(options.HeartbeatTimeout),
            options.HeartbeatTimeout);

        if (options.HeartbeatTimeout > options.HeartbeatInterval)
        {
            failures.Add(
                $"{nameof(options.HeartbeatTimeout)} must not exceed {nameof(options.HeartbeatInterval)}.");
        }

        AddRangeFailureIfInvalid(
            failures,
            nameof(options.MaxIncomingMessageSize),
            options.MaxIncomingMessageSize,
            AbsoluteMaxIncomingMessageSize);
        AddRangeFailureIfInvalid(
            failures,
            nameof(options.MaxOutgoingMessageSize),
            options.MaxOutgoingMessageSize,
            AbsoluteMaxOutgoingMessageSize);
        AddRangeFailureIfInvalid(
            failures,
            nameof(options.OutgoingBufferCapacity),
            options.OutgoingBufferCapacity,
            AbsoluteMaxOutgoingBufferCapacity);
        AddRangeFailureIfInvalid(
            failures,
            nameof(options.MaxSubscriptionsPerConnection),
            options.MaxSubscriptionsPerConnection,
            AbsoluteMaxSubscriptionsPerConnection);
        AddRangeFailureIfInvalid(
            failures,
            nameof(options.MaxSubscriptionKeyLength),
            options.MaxSubscriptionKeyLength,
            AbsoluteMaxSubscriptionKeyLength);

        if (!Enum.IsDefined(options.SlowClientPolicy))
        {
            failures.Add($"{nameof(options.SlowClientPolicy)} has the unsupported value {options.SlowClientPolicy}.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void AddDurationFailureIfInvalid(
        List<string> failures,
        string propertyName,
        TimeSpan value)
    {
        if (value <= TimeSpan.Zero || value > MaximumHeartbeatDuration)
        {
            failures.Add($"{propertyName} must be greater than zero and no more than {MaximumHeartbeatDuration}.");
        }
    }

    private static void AddRangeFailureIfInvalid(
        List<string> failures,
        string propertyName,
        int value,
        int maximum)
    {
        if (value < 1 || value > maximum)
        {
            failures.Add($"{propertyName} must be between 1 and {maximum}; configured value was {value}.");
        }
    }
}
