using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace WebSocketNotifications.Tests.Hosting;

public sealed class NotificationMessageSourceWorkerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 7, 8, 9, 10, TimeSpan.Zero);

    [Fact]
    public async Task RunSourceAsync_WhenSourceEmitsNotification_AwaitsSuccessfulDelivery()
    {
        var registry = new ConnectionRegistry();
        var buffer = new ConnectionBuffer(1, SlowClientPolicy.Disconnect);
        registry.Add("connection-1", "user-1", buffer, requestStop: null);
        var source = new ImmediateSource(CreateNotification(payloadSize: 1));
        var worker = CreateWorker(source, registry, maxOutgoingSize: 4096);

        await worker.RunSourceAsync(CancellationToken.None);

        Assert.True(source.HandlerCompleted);
        Assert.True(buffer.TryRead(out _));
    }

    [Fact]
    public async Task RunSourceAsync_WhenNotificationHandlerFails_PropagatesFailureToSource()
    {
        var registry = new ConnectionRegistry();
        registry.Add(
            "connection-1",
            "user-1",
            new ConnectionBuffer(1, SlowClientPolicy.Disconnect),
            requestStop: null);
        var source = new ImmediateSource(CreateNotification(payloadSize: 100));
        var worker = CreateWorker(source, registry, maxOutgoingSize: 32);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => worker.RunSourceAsync(CancellationToken.None));

        Assert.Contains("MaxOutgoingMessageSize", error.Message, StringComparison.Ordinal);
        Assert.False(source.HandlerCompleted);
    }

    [Fact]
    public async Task RunSourceAsync_WhenStopping_CancellationReachesSource()
    {
        var source = new WaitingSource();
        var worker = CreateWorker(source, new ConnectionRegistry(), maxOutgoingSize: 4096);
        using var cancellation = new CancellationTokenSource();
        var run = worker.RunSourceAsync(cancellation.Token);

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.True(source.ObservedCancellation);
    }

    [Fact]
    public async Task RunSourceAsync_WhenNoSourceIsRegistered_CompletesWithoutStartingInfrastructure()
    {
        var worker = CreateWorker([], new ConnectionRegistry(), maxOutgoingSize: 4096);

        await worker.RunSourceAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RunSourceAsync_WhenMultipleSourcesAreRegistered_RejectsAmbiguousOrdering()
    {
        var source = new ImmediateSource(CreateNotification(payloadSize: 1));
        var worker = CreateWorker([source, source], new ConnectionRegistry(), maxOutgoingSize: 4096);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => worker.RunSourceAsync(CancellationToken.None));
    }

    [Fact]
    public void CoreAssembly_HasNoBrokerSpecificDependency()
    {
        var names = typeof(INotificationMessageSource).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToArray();

        Assert.DoesNotContain(names, name =>
            name is not null &&
            (name.Contains("Kafka", StringComparison.OrdinalIgnoreCase) ||
             name.Contains("RabbitMQ", StringComparison.OrdinalIgnoreCase) ||
             name.Contains("NetMQ", StringComparison.OrdinalIgnoreCase) ||
             name.Contains("Redis", StringComparison.OrdinalIgnoreCase)));
    }

    private static NotificationMessageSourceWorker CreateWorker(
        INotificationMessageSource source,
        ConnectionRegistry registry,
        int maxOutgoingSize) => CreateWorker([source], registry, maxOutgoingSize);

    private static NotificationMessageSourceWorker CreateWorker(
        IEnumerable<INotificationMessageSource> sources,
        ConnectionRegistry registry,
        int maxOutgoingSize)
    {
        var router = new NotificationRouter(registry);
        var hub = new WebSocketNotificationHub(
            registry,
            new NotificationDispatcher(registry, router, maxOutgoingSize),
            new FixedTimeProvider(Now));
        return new NotificationMessageSourceWorker(
            sources,
            new Lazy<WebSocketNotificationHub>(() => hub),
            NullLogger<NotificationMessageSourceWorker>.Instance);
    }

    private static NotificationEnvelope CreateNotification(int payloadSize) =>
        new(
            "message-1",
            JsonSerializer.SerializeToElement(new { text = new string('x', payloadSize) }),
            Now.AddMinutes(-1),
            userIds: ["user-1"]);

    private sealed class ImmediateSource(NotificationEnvelope notification) : INotificationMessageSource
    {
        public bool HandlerCompleted { get; private set; }

        public async Task RunAsync(
            Func<NotificationEnvelope, CancellationToken, Task> handler,
            CancellationToken cancellationToken)
        {
            await handler(notification, cancellationToken);
            HandlerCompleted = true;
        }
    }

    private sealed class WaitingSource : INotificationMessageSource
    {
        public bool ObservedCancellation { get; private set; }

        public async Task RunAsync(
            Func<NotificationEnvelope, CancellationToken, Task> handler,
            CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ObservedCancellation = true;
                throw;
            }
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
