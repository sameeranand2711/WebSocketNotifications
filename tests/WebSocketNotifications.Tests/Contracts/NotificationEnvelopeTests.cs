using System.Text.Json;
using Xunit;

namespace WebSocketNotifications.Tests.Contracts;

public sealed class NotificationEnvelopeTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Fact]
    public void Constructor_WhenRoutingIsMissing_RejectsEnvelope()
    {
        Assert.Throws<ArgumentException>(() => Create());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WhenMessageIdIsMissing_RejectsEnvelope(string? messageId)
    {
        Assert.Throws<ArgumentException>(() => Create(messageId: messageId, userIds: ["user-1"]));
    }

    [Fact]
    public void Constructor_WhenTimestampsAreNotUtc_RejectsEnvelope()
    {
        var localTime = CreatedAt.ToOffset(TimeSpan.FromHours(5.5));

        Assert.Throws<ArgumentException>(() => Create(createdAt: localTime, userIds: ["user-1"]));
        Assert.Throws<ArgumentException>(() => Create(expiresAt: localTime, userIds: ["user-1"]));
    }

    [Fact]
    public void Constructor_WhenRoutingEntryIsBlank_RejectsEnvelope()
    {
        Assert.Throws<ArgumentException>(() => Create(subscriptions: ["group:premium", " "]));
    }

    [Fact]
    public void Constructor_CopiesRoutingCollectionsAndUsesSafeEmptyCollections()
    {
        var users = new[] { "user-1" };
        var subscriptions = new[] { "group:premium" };
        var envelope = Create(userIds: users, subscriptions: subscriptions);

        users[0] = "changed";
        subscriptions[0] = "changed";

        Assert.Equal(["user-1"], envelope.UserIds);
        Assert.Equal(["group:premium"], envelope.Subscriptions);
    }

    [Fact]
    public void Constructor_AllowsUsersOrSubscriptionsIndependently()
    {
        var direct = Create(userIds: ["user-1"]);
        var subscribed = Create(subscriptions: ["tenant:abc:group:premium"]);

        Assert.Equal(["user-1"], direct.UserIds);
        Assert.Empty(direct.Subscriptions);
        Assert.Empty(subscribed.UserIds);
        Assert.Equal(["tenant:abc:group:premium"], subscribed.Subscriptions);
    }

    [Fact]
    public void IsExpired_UsesInclusiveUtcExpiryBoundary()
    {
        var expiry = CreatedAt.AddMinutes(1);
        var envelope = Create(expiresAt: expiry, subscriptions: ["feed:scores"]);

        Assert.False(envelope.IsExpired(expiry.AddTicks(-1)));
        Assert.True(envelope.IsExpired(expiry));
        Assert.True(envelope.IsExpired(expiry.AddTicks(1)));
    }

    [Fact]
    public void Constructor_ClonesPayloadFromItsSourceDocument()
    {
        JsonElement payload;
        using (var document = JsonDocument.Parse("{\"score\":42}"))
        {
            payload = document.RootElement;
            var envelope = Create(payload: payload, subscriptions: ["event:score.changed"]);
            payload = envelope.Payload;
        }

        Assert.Equal(42, payload.GetProperty("score").GetInt32());
    }

    private static NotificationEnvelope Create(
        string? messageId = "message-1",
        JsonElement? payload = null,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? expiresAt = null,
        IEnumerable<string>? userIds = null,
        IEnumerable<string>? subscriptions = null) =>
        new(
            messageId!,
            payload ?? JsonSerializer.SerializeToElement(new { text = "hello" }),
            createdAt ?? CreatedAt,
            expiresAt,
            userIds,
            subscriptions);
}
