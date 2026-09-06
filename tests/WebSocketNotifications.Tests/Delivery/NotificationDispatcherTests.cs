using System.Text;
using System.Text.Json;
using Xunit;

namespace WebSocketNotifications.Tests.Delivery;

public sealed class NotificationDispatcherTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

    [Fact]
    public async Task DispatchAsync_SerializesNotificationFrameAndEnqueuesRecipient()
    {
        var registry = new ConnectionRegistry();
        var buffer = new ConnectionBuffer(1, SlowClientPolicy.Disconnect);
        registry.Add("connection-1", "user-1", buffer, requestStop: null);
        var dispatcher = CreateDispatcher(registry);
        var notification = CreateNotification("message-42", ["user-1"], new { score = 7 });

        await dispatcher.DispatchAsync(notification, Now);

        Assert.True(buffer.TryRead(out var message));
        using var document = JsonDocument.Parse(message);
        var root = document.RootElement;
        Assert.Equal("notification", root.GetProperty("type").GetString());
        Assert.Equal("message-42", root.GetProperty("messageId").GetString());
        Assert.Equal(7, root.GetProperty("payload").GetProperty("score").GetInt32());
        Assert.Equal(Now.AddMinutes(-1), root.GetProperty("createdAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task DispatchAsync_WhenFrameExceedsOutgoingLimit_RejectsBeforeEnqueue()
    {
        var registry = new ConnectionRegistry();
        var buffer = new ConnectionBuffer(1, SlowClientPolicy.Disconnect);
        registry.Add("connection-1", "user-1", buffer, requestStop: null);
        var dispatcher = CreateDispatcher(registry, maxOutgoingMessageSize: 32);
        var notification = CreateNotification("message-1", ["user-1"], new { text = new string('x', 100) });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.DispatchAsync(notification, Now).AsTask());

        Assert.Contains("MaxOutgoingMessageSize", error.Message, StringComparison.Ordinal);
        Assert.False(buffer.TryRead(out _));
    }

    [Fact]
    public async Task DispatchAsync_WhenOneClientOverflows_DisconnectsItAndContinuesToHealthyClient()
    {
        var registry = new ConnectionRegistry();
        var slowBuffer = new ConnectionBuffer(1, SlowClientPolicy.Disconnect);
        var healthyBuffer = new ConnectionBuffer(2, SlowClientPolicy.Disconnect);
        var slowStopRequests = 0;
        registry.Add("slow", "user-1", slowBuffer, () => slowStopRequests++);
        registry.Add("healthy", "user-1", healthyBuffer, requestStop: null);
        slowBuffer.TryEnqueue(Encoding.UTF8.GetBytes("already-full"));

        await CreateDispatcher(registry).DispatchAsync(
            CreateNotification("message-1", ["user-1"], new { value = 1 }),
            Now);

        Assert.Equal(1, slowStopRequests);
        Assert.DoesNotContain("slow", registry.GetConnectionIdsForUser("user-1"));
        Assert.True(healthyBuffer.TryRead(out var delivered));
        Assert.Contains("message-1", Encoding.UTF8.GetString(delivered.Span), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DispatchAsync_WhenNotificationExpired_DoesNotSerializeOrEnqueue()
    {
        var registry = new ConnectionRegistry();
        var buffer = new ConnectionBuffer(1, SlowClientPolicy.Disconnect);
        registry.Add("connection-1", "user-1", buffer, requestStop: null);
        var notification = new NotificationEnvelope(
            "message-1",
            JsonSerializer.SerializeToElement(new { value = 1 }),
            Now.AddMinutes(-2),
            Now,
            userIds: ["user-1"]);

        await CreateDispatcher(registry).DispatchAsync(notification, Now);

        Assert.False(buffer.TryRead(out _));
    }

    private static NotificationDispatcher CreateDispatcher(
        ConnectionRegistry registry,
        int maxOutgoingMessageSize = 4096) =>
        new(registry, new NotificationRouter(registry), maxOutgoingMessageSize);

    private static NotificationEnvelope CreateNotification(
        string messageId,
        IEnumerable<string> userIds,
        object payload) =>
        new(
            messageId,
            JsonSerializer.SerializeToElement(payload),
            Now.AddMinutes(-1),
            userIds: userIds);
}
