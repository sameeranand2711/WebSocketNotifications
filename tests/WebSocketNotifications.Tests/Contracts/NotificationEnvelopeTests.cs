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
        Assert.Throws<ArgumentException>(() => Create(groups: ["group-1", " "]));
    }

    [Fact]
    public void Constructor_CopiesRoutingCollectionsAndUsesSafeEmptyCollections()
    {
        var users = new[] { "user-1" };
        var envelope = Create(userIds: users);

        users[0] = "changed";

        Assert.Equal(["user-1"], envelope.UserIds);
        Assert.Empty(envelope.Groups);
        Assert.Empty(envelope.Feeds);
        Assert.Empty(envelope.EventTypes);
    }

    [Fact]
    public void IsExpired_UsesInclusiveUtcExpiryBoundary()
    {
        var expiry = CreatedAt.AddMinutes(1);
        var envelope = Create(expiresAt: expiry, feeds: ["scores"]);

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
            var envelope = Create(payload: payload, eventTypes: ["score.changed"]);
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
        IEnumerable<string>? groups = null,
        IEnumerable<string>? feeds = null,
        IEnumerable<string>? eventTypes = null) =>
        new(
            messageId!,
            payload ?? JsonSerializer.SerializeToElement(new { text = "hello" }),
            createdAt ?? CreatedAt,
            expiresAt,
            userIds,
            groups,
            feeds,
            eventTypes);
}
