using System.Text.Json;
using System.Text.Json.Serialization;
using WebSocketNotifications.Abstractions;
using WebSocketNotifications.Connections;

namespace WebSocketNotifications.Protocol;

/// <summary>
/// Validates inbound protocol messages, applies subscription authorization, and emits protocol responses.
/// </summary>
internal sealed class ProtocolProcessor(
    ConnectionRegistry registry,
    ISubscriptionAuthorizer authorizer,
    IWebSocketInboundMessageHandler? inboundHandler,
    HeartbeatState? heartbeatState = null)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async ValueTask ProcessAsync(
        string connectionId,
        string userId,
        ReadOnlyMemory<byte> message,
        ConnectionBuffer outgoing,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(message);
        }
        catch (JsonException)
        {
            EnqueueError(connectionId, outgoing, requestId: null, "invalid_message", "The message is not valid JSON.");
            return;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !TryGetString(root, "type", out var type))
            {
                EnqueueError(connectionId, outgoing, requestId: null, "invalid_message", "A string type is required.");
                return;
            }

            var requestId = TryGetString(root, "requestId", out var parsedRequestId)
                ? parsedRequestId
                : null;

            // Only library-owned message types are intercepted. Unknown types intentionally flow
            // to the optional application handler so applications can extend the inbound protocol.
            switch (type)
            {
                case "subscribe":
                    await SubscribeAsync(
                            connectionId,
                            userId,
                            root,
                            requestId,
                            outgoing,
                            cancellationToken)
                        .ConfigureAwait(false);
                    break;

                case "unsubscribe":
                    Unsubscribe(connectionId, root, requestId, outgoing);
                    break;

                case "pong":
                    if (!TryGetString(root, "nonce", out var nonce))
                    {
                        EnqueueError(
                            connectionId,
                            outgoing,
                            requestId,
                            "invalid_message",
                            "A heartbeat pong requires a string nonce.");
                    }
                    else
                    {
                        heartbeatState?.Acknowledge(nonce);
                    }

                    break;

                default:
                    if (inboundHandler is not null)
                    {
                        await inboundHandler.HandleAsync(
                                new InboundWebSocketMessage(connectionId, userId, root.Clone()),
                                cancellationToken)
                            .ConfigureAwait(false);
                    }

                    break;
            }
        }
    }

    private async ValueTask SubscribeAsync(
        string connectionId,
        string userId,
        JsonElement root,
        string? requestId,
        ConnectionBuffer outgoing,
        CancellationToken cancellationToken)
    {
        if (!TryGetSubscriptions(root, out var subscriptions))
        {
            EnqueueError(
                connectionId,
                outgoing,
                requestId,
                "invalid_subscription",
                "Subscriptions must be a non-empty array of nonblank strings.");
            return;
        }

        if (RejectOverlongSubscription(connectionId, subscriptions, requestId, outgoing))
        {
            return;
        }

        // Authorize the complete request before mutating the registry so a denied key cannot
        // leave an earlier key from the same command partially subscribed.
        foreach (var subscription in subscriptions)
        {
            var context = new SubscriptionAuthorizationContext(connectionId, userId, subscription);
            if (!await authorizer.AuthorizeAsync(context, cancellationToken).ConfigureAwait(false))
            {
                EnqueueError(
                    connectionId,
                    outgoing,
                    requestId,
                    "subscription_denied",
                    "The application denied this subscription.");
                return;
            }
        }

        var result = registry.AddSubscriptions(connectionId, subscriptions);
        if (result == SubscriptionAddResult.LimitExceeded)
        {
            EnqueueError(
                connectionId,
                outgoing,
                requestId,
                "subscription_limit_exceeded",
                "The connection subscription limit would be exceeded.");
            return;
        }

        if (result == SubscriptionAddResult.KeyTooLong)
        {
            EnqueueError(
                connectionId,
                outgoing,
                requestId,
                "subscription_key_too_long",
                $"A subscription exceeds MaxSubscriptionKeyLength ({registry.MaxSubscriptionKeyLength}).");
            return;
        }

        Enqueue(connectionId, outgoing, new ProtocolResponse("subscribed", requestId));
    }

    private void Unsubscribe(
        string connectionId,
        JsonElement root,
        string? requestId,
        ConnectionBuffer outgoing)
    {
        if (!TryGetSubscriptions(root, out var subscriptions))
        {
            EnqueueError(
                connectionId,
                outgoing,
                requestId,
                "invalid_subscription",
                "Subscriptions must be a non-empty array of nonblank strings.");
            return;
        }

        if (RejectOverlongSubscription(connectionId, subscriptions, requestId, outgoing))
        {
            return;
        }

        foreach (var subscription in subscriptions)
        {
            registry.RemoveSubscription(connectionId, subscription);
        }

        Enqueue(connectionId, outgoing, new ProtocolResponse("unsubscribed", requestId));
    }

    private bool RejectOverlongSubscription(
        string connectionId,
        IReadOnlyList<string> subscriptions,
        string? requestId,
        ConnectionBuffer outgoing)
    {
        if (!subscriptions.Any(subscription => subscription.Length > registry.MaxSubscriptionKeyLength))
        {
            return false;
        }

        EnqueueError(
            connectionId,
            outgoing,
            requestId,
            "subscription_key_too_long",
            $"A subscription exceeds MaxSubscriptionKeyLength ({registry.MaxSubscriptionKeyLength}).");
        return true;
    }

    private void EnqueueError(
        string connectionId,
        ConnectionBuffer outgoing,
        string? requestId,
        string code,
        string message) =>
        Enqueue(connectionId, outgoing, new ProtocolResponse("error", requestId, code, message));

    private void Enqueue(string connectionId, ConnectionBuffer outgoing, ProtocolResponse response)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(response, SerializerOptions);
        if (outgoing.TryEnqueue(bytes) == BufferWriteResult.Disconnect)
        {
            registry.Disconnect(connectionId);
        }
    }

    private static bool TryGetSubscriptions(
        JsonElement root,
        out IReadOnlyList<string> subscriptions)
    {
        subscriptions = [];
        if (!root.TryGetProperty("subscriptions", out var property) ||
            property.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var values = new List<string>();
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in property.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(element.GetString()))
            {
                return false;
            }

            var value = element.GetString()!;
            if (unique.Add(value))
            {
                values.Add(value);
            }
        }

        subscriptions = values;
        return values.Count > 0;
    }

    private static bool TryGetString(JsonElement root, string propertyName, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString()!;
        return value is not null;
    }

    private sealed record ProtocolResponse(
        string Type,
        string? RequestId,
        string? Code = null,
        string? Message = null);
}

internal sealed class DenyAllSubscriptionAuthorizer : ISubscriptionAuthorizer
{
    // Secure default: applications must opt in before clients can join routing scopes.
    public ValueTask<bool> AuthorizeAsync(
        SubscriptionAuthorizationContext context,
        CancellationToken cancellationToken) => ValueTask.FromResult(false);
}
