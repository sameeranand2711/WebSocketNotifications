using System.Text;
using System.Text.Json;
using Xunit;

namespace WebSocketNotifications.Tests.Protocol;

public sealed class ProtocolProcessorTests
{
    [Fact]
    public async Task ProcessAsync_WhenSubscribeIsAuthorized_AddsSubscriptionAndConfirms()
    {
        var authorizer = new RecordingAuthorizer(allowed: true);
        var (registry, outgoing, processor) = CreateProcessor(authorizer);

        await processor.ProcessAsync(
            "connection-1",
            "user-1",
            Json("""{"type":"subscribe","requestId":"r1","subscriptions":["tenant:abc:group:operators"]}"""),
            outgoing,
            CancellationToken.None);

        Assert.Equal(
            ["tenant:abc:group:operators"],
            registry.GetSubscriptions("connection-1"));
        var authorization = Assert.Single(authorizer.Contexts);
        Assert.Equal("user-1", authorization.UserId);
        Assert.Equal("tenant:abc:group:operators", authorization.Subscription);
        AssertResponse(outgoing, "subscribed", "r1");
    }

    [Fact]
    public async Task ProcessAsync_WhenSubscribeIsDenied_DoesNotMutateStateAndReturnsError()
    {
        var (registry, outgoing, processor) = CreateProcessor(new RecordingAuthorizer(allowed: false));

        await processor.ProcessAsync(
            "connection-1",
            "user-1",
            Json("""{"type":"subscribe","requestId":"r2","subscriptions":["partner:p1:feed:private"]}"""),
            outgoing,
            CancellationToken.None);

        Assert.Empty(registry.GetSubscriptions("connection-1"));
        AssertError(outgoing, "r2", "subscription_denied");
    }

    [Fact]
    public async Task ProcessAsync_WhenSubscribeContainsDuplicateKeys_AuthorizesAndAddsEachKeyOnce()
    {
        var authorizer = new RecordingAuthorizer(allowed: true);
        var (registry, outgoing, processor) = CreateProcessor(authorizer);

        await processor.ProcessAsync(
            "connection-1",
            "user-1",
            Json("""{"type":"subscribe","requestId":"batch-1","subscriptions":["group:operators","feed:alerts","group:operators"]}"""),
            outgoing,
            CancellationToken.None);

        Assert.Equal(["feed:alerts", "group:operators"], registry.GetSubscriptions("connection-1").Order());
        Assert.Equal(["group:operators", "feed:alerts"], authorizer.Contexts.Select(context => context.Subscription));
        AssertResponse(outgoing, "subscribed", "batch-1");
    }

    [Fact]
    public async Task ProcessAsync_WhenAnySubscriptionIsDenied_DoesNotPartiallyMutateState()
    {
        var authorizer = new PredicateAuthorizer(subscription => subscription != "group:denied");
        var (registry, outgoing, processor) = CreateProcessor(authorizer);

        await processor.ProcessAsync(
            "connection-1",
            "user-1",
            Json("""{"type":"subscribe","requestId":"batch-2","subscriptions":["feed:allowed","group:denied"]}"""),
            outgoing,
            CancellationToken.None);

        Assert.Empty(registry.GetSubscriptions("connection-1"));
        AssertError(outgoing, "batch-2", "subscription_denied");
    }

    [Fact]
    public async Task ProcessAsync_WhenSubscribeReachesLimitExactly_AddsWholeBatch()
    {
        var (registry, outgoing, processor) = CreateProcessor(
            new RecordingAuthorizer(allowed: true),
            maxSubscriptions: 2);

        await processor.ProcessAsync(
            "connection-1",
            "user-1",
            Json("""{"type":"subscribe","requestId":"limit","subscriptions":["first","second"]}"""),
            outgoing,
            CancellationToken.None);

        Assert.Equal(["first", "second"], registry.GetSubscriptions("connection-1").Order());
        AssertResponse(outgoing, "subscribed", "limit");
    }

    [Fact]
    public async Task ProcessAsync_WhenSubscribeExceedsLimit_RejectsBatchAtomically()
    {
        var authorizer = new RecordingAuthorizer(allowed: true);
        var (registry, outgoing, processor) = CreateProcessor(
            authorizer,
            maxSubscriptions: 2);

        await processor.ProcessAsync(
            "connection-1",
            "user-1",
            Json("""{"type":"subscribe","requestId":"over-limit","subscriptions":["first","second","third"]}"""),
            outgoing,
            CancellationToken.None);

        Assert.Empty(registry.GetSubscriptions("connection-1"));
        Assert.Equal(3, authorizer.Contexts.Count);
        AssertError(outgoing, "over-limit", "subscription_limit_exceeded");
    }

    [Fact]
    public async Task ProcessAsync_WhenExistingSubscriptionIsRepeated_DuplicateDoesNotConsumeQuota()
    {
        var (registry, outgoing, processor) = CreateProcessor(
            new RecordingAuthorizer(allowed: true),
            maxSubscriptions: 2);
        registry.AddSubscription("connection-1", "existing");

        await processor.ProcessAsync(
            "connection-1",
            "user-1",
            Json("""{"type":"subscribe","requestId":"duplicate","subscriptions":["existing","new"]}"""),
            outgoing,
            CancellationToken.None);

        Assert.Equal(["existing", "new"], registry.GetSubscriptions("connection-1").Order());
        AssertResponse(outgoing, "subscribed", "duplicate");
    }

    [Fact]
    public async Task ProcessAsync_WhenAnySubscriptionKeyIsTooLong_RejectsBatchBeforeAuthorization()
    {
        var authorizer = new RecordingAuthorizer(allowed: true);
        var (registry, outgoing, processor) = CreateProcessor(
            authorizer,
            maxKeyLength: 5);

        await processor.ProcessAsync(
            "connection-1",
            "user-1",
            Json("""{"type":"subscribe","requestId":"key-length","subscriptions":["12345","123456"]}"""),
            outgoing,
            CancellationToken.None);

        Assert.Empty(registry.GetSubscriptions("connection-1"));
        Assert.Empty(authorizer.Contexts);
        AssertError(outgoing, "key-length", "subscription_key_too_long");
    }

    [Fact]
    public async Task ProcessAsync_WhenUnsubscribeFreesQuota_AllowsResubscribe()
    {
        var (registry, outgoing, processor) = CreateProcessor(
            new RecordingAuthorizer(allowed: true),
            maxSubscriptions: 1);
        registry.AddSubscription("connection-1", "first");

        await processor.ProcessAsync(
            "connection-1",
            "user-1",
            Json("""{"type":"unsubscribe","requestId":"remove","subscriptions":["first"]}"""),
            outgoing,
            CancellationToken.None);
        AssertResponse(outgoing, "unsubscribed", "remove");
        await processor.ProcessAsync(
            "connection-1",
            "user-1",
            Json("""{"type":"subscribe","requestId":"replace","subscriptions":["second"]}"""),
            outgoing,
            CancellationToken.None);

        Assert.Equal(["second"], registry.GetSubscriptions("connection-1"));
        AssertResponse(outgoing, "subscribed", "replace");
    }

    [Fact]
    public async Task ProcessAsync_WhenUnsubscribing_RemovesSubscriptionWithoutAuthorization()
    {
        var authorizer = new RecordingAuthorizer(allowed: false);
        var (registry, outgoing, processor) = CreateProcessor(authorizer);
        registry.AddSubscription("connection-1", "event:score.changed");
        registry.AddSubscription("connection-1", "group:operators");

        await processor.ProcessAsync(
            "connection-1",
            "user-1",
            Json("""{"type":"unsubscribe","requestId":"r3","subscriptions":["event:score.changed"]}"""),
            outgoing,
            CancellationToken.None);

        Assert.Equal(["group:operators"], registry.GetSubscriptions("connection-1"));
        Assert.Empty(authorizer.Contexts);
        AssertResponse(outgoing, "unsubscribed", "r3");
    }

    [Fact]
    public async Task ProcessAsync_WhenSubscriptionsAreMissing_RejectsCommand()
    {
        var (registry, outgoing, processor) = CreateProcessor(new RecordingAuthorizer(allowed: true));

        await processor.ProcessAsync(
            "connection-1",
            "user-1",
            Json("""{"type":"subscribe","requestId":"r4"}"""),
            outgoing,
            CancellationToken.None);

        Assert.Empty(registry.GetSubscriptions("connection-1"));
        AssertError(outgoing, "r4", "invalid_subscription");
    }

    [Fact]
    public async Task ProcessAsync_WhenBatchMixesValidAndBlankKeys_RejectsBeforeAuthorization()
    {
        var authorizer = new RecordingAuthorizer(allowed: true);
        var (registry, outgoing, processor) = CreateProcessor(authorizer);

        await processor.ProcessAsync(
            "connection-1",
            "user-1",
            Json("""{"type":"subscribe","requestId":"mixed","subscriptions":["valid"," "]}"""),
            outgoing,
            CancellationToken.None);

        Assert.Empty(registry.GetSubscriptions("connection-1"));
        Assert.Empty(authorizer.Contexts);
        AssertError(outgoing, "mixed", "invalid_subscription");
    }

    [Fact]
    public async Task ProcessAsync_WhenUnsubscribeKeyIsTooLong_RejectsCommand()
    {
        var (registry, outgoing, processor) = CreateProcessor(
            new RecordingAuthorizer(allowed: true),
            maxKeyLength: 5);

        await processor.ProcessAsync(
            "connection-1",
            "user-1",
            Json("""{"type":"unsubscribe","requestId":"long-remove","subscriptions":["123456"]}"""),
            outgoing,
            CancellationToken.None);

        Assert.Empty(registry.GetSubscriptions("connection-1"));
        AssertError(outgoing, "long-remove", "subscription_key_too_long");
    }

    [Fact]
    public async Task ProcessAsync_WhenMessageIsApplicationSpecific_SurfacesSafePayload()
    {
        var handler = new RecordingInboundHandler();
        var (_, outgoing, processor) = CreateProcessor(new RecordingAuthorizer(true), handler);

        await processor.ProcessAsync(
            "connection-1",
            "user-1",
            Json("""{"type":"chat.typing","room":"support"}"""),
            outgoing,
            CancellationToken.None);

        var message = Assert.Single(handler.Messages);
        Assert.Equal("connection-1", message.ConnectionId);
        Assert.Equal("user-1", message.UserId);
        Assert.Equal("support", message.Payload.GetProperty("room").GetString());
        Assert.False(outgoing.TryRead(out _));
    }

    [Fact]
    public async Task ProcessAsync_WhenJsonIsMalformed_ReturnsProtocolError()
    {
        var (_, outgoing, processor) = CreateProcessor(new RecordingAuthorizer(true));

        await processor.ProcessAsync(
            "connection-1",
            "user-1",
            Json("{"),
            outgoing,
            CancellationToken.None);

        AssertError(outgoing, requestId: null, "invalid_message");
    }

    [Fact]
    public async Task DenyAllAuthorizer_IsSecureExplicitDefault()
    {
        var allowed = await new DenyAllSubscriptionAuthorizer().AuthorizeAsync(
            new SubscriptionAuthorizationContext(
                "connection-1",
                "user-1",
                "group:operators"),
            CancellationToken.None);

        Assert.False(allowed);
    }

    private static (ConnectionRegistry Registry, ConnectionBuffer Outgoing, ProtocolProcessor Processor)
        CreateProcessor(
            ISubscriptionAuthorizer authorizer,
            IWebSocketInboundMessageHandler? handler = null,
            int maxSubscriptions = 128,
            int maxKeyLength = 256)
    {
        var registry = new ConnectionRegistry(maxSubscriptions, maxKeyLength);
        registry.Add("connection-1", "user-1");
        var outgoing = new ConnectionBuffer(8, SlowClientPolicy.Disconnect);
        return (registry, outgoing, new ProtocolProcessor(registry, authorizer, handler));
    }

    private static ReadOnlyMemory<byte> Json(string value) => Encoding.UTF8.GetBytes(value);

    private static void AssertResponse(ConnectionBuffer outgoing, string type, string requestId)
    {
        Assert.True(outgoing.TryRead(out var response));
        using var document = JsonDocument.Parse(response);
        Assert.Equal(type, document.RootElement.GetProperty("type").GetString());
        Assert.Equal(requestId, document.RootElement.GetProperty("requestId").GetString());
    }

    private static void AssertError(ConnectionBuffer outgoing, string? requestId, string code)
    {
        Assert.True(outgoing.TryRead(out var response));
        using var document = JsonDocument.Parse(response);
        Assert.Equal("error", document.RootElement.GetProperty("type").GetString());
        Assert.Equal(code, document.RootElement.GetProperty("code").GetString());
        if (requestId is not null)
        {
            Assert.Equal(requestId, document.RootElement.GetProperty("requestId").GetString());
        }
    }

    private sealed class RecordingAuthorizer(bool allowed) : ISubscriptionAuthorizer
    {
        public List<SubscriptionAuthorizationContext> Contexts { get; } = [];

        public ValueTask<bool> AuthorizeAsync(
            SubscriptionAuthorizationContext context,
            CancellationToken cancellationToken)
        {
            Contexts.Add(context);
            return ValueTask.FromResult(allowed);
        }
    }

    private sealed class RecordingInboundHandler : IWebSocketInboundMessageHandler
    {
        public List<InboundWebSocketMessage> Messages { get; } = [];

        public ValueTask HandleAsync(
            InboundWebSocketMessage message,
            CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class PredicateAuthorizer(Func<string, bool> isAllowed) : ISubscriptionAuthorizer
    {
        public ValueTask<bool> AuthorizeAsync(
            SubscriptionAuthorizationContext context,
            CancellationToken cancellationToken) => ValueTask.FromResult(isAllowed(context.Subscription));
    }
}
