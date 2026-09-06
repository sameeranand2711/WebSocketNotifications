using System.Text.Json;
using Xunit;

namespace WebSocketNotifications.Tests.Connections;

public sealed class HeartbeatTests
{
    private static readonly DateTimeOffset Now = new(2026, 4, 5, 6, 7, 8, TimeSpan.Zero);

    [Fact]
    public void HasTimedOut_WhenMatchingPongIsMissing_UsesInclusiveTimeoutBoundary()
    {
        var state = new HeartbeatState();
        state.BeginProbe("nonce-1", Now);

        Assert.False(state.HasTimedOut(Now.AddSeconds(9), TimeSpan.FromSeconds(10)));
        Assert.True(state.HasTimedOut(Now.AddSeconds(10), TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void Acknowledge_OnlyMatchingPongClearsPendingProbe()
    {
        var state = new HeartbeatState();
        state.BeginProbe("nonce-1", Now);

        Assert.False(state.Acknowledge("other"));
        Assert.True(state.HasTimedOut(Now.AddSeconds(10), TimeSpan.FromSeconds(10)));
        Assert.True(state.Acknowledge("nonce-1"));
        Assert.False(state.HasTimedOut(Now.AddHours(1), TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task ProcessAsync_WhenPongIsReceived_AcknowledgesHeartbeat()
    {
        var state = new HeartbeatState();
        state.BeginProbe("nonce-1", Now);
        var registry = new ConnectionRegistry();
        registry.Add("connection-1", "user-1");
        var processor = new ProtocolProcessor(
            registry,
            new DenyAllSubscriptionAuthorizer(),
            inboundHandler: null,
            state);

        await processor.ProcessAsync(
            "connection-1",
            "user-1",
            """{"type":"pong","nonce":"nonce-1"}"""u8.ToArray(),
            new ConnectionBuffer(1, SlowClientPolicy.Disconnect),
            CancellationToken.None);

        Assert.False(state.HasTimedOut(Now.AddHours(1), TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task RunAsync_WhenPongIsMissing_DisconnectsConnection()
    {
        var registry = new ConnectionRegistry();
        var outgoing = new ConnectionBuffer(2, SlowClientPolicy.Disconnect);
        var stopRequests = 0;
        registry.Add("connection-1", "user-1", outgoing, () => stopRequests++);
        var loop = new HeartbeatLoop(
            "connection-1",
            registry,
            outgoing,
            new HeartbeatState(),
            TimeSpan.FromMilliseconds(40),
            TimeSpan.FromMilliseconds(20),
            TimeProvider.System);

        await loop.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, stopRequests);
        Assert.Empty(registry.GetConnectionIdsForUser("user-1"));
        Assert.True(outgoing.TryRead(out var ping));
        using var document = JsonDocument.Parse(ping);
        Assert.Equal("ping", document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task RunAsync_WhenPongsMatch_KeepsConnectionUntilCancellation()
    {
        var registry = new ConnectionRegistry();
        var outgoing = new ConnectionBuffer(2, SlowClientPolicy.Disconnect);
        var state = new HeartbeatState();
        registry.Add("connection-1", "user-1", outgoing, requestStop: null);
        var loop = new HeartbeatLoop(
            "connection-1",
            registry,
            outgoing,
            state,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(2),
            TimeProvider.System);
        using var cancellation = new CancellationTokenSource();
        var run = loop.RunAsync(cancellation.Token);

        var ping = await outgoing.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        using var document = JsonDocument.Parse(ping);
        Assert.True(state.Acknowledge(document.RootElement.GetProperty("nonce").GetString()!));

        cancellation.Cancel();
        await run;
        Assert.Equal(["connection-1"], registry.GetConnectionIdsForUser("user-1"));
    }
}
