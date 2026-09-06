using System.Threading.Channels;
using Microsoft.Extensions.Options;
using WebSocketNotifications.Abstractions;
using WebSocketNotifications.Contracts;

namespace WebSocketNotifications.Host.Kafka;

/// <summary>Provides a bounded, acknowledgement-aware bridge from Kafka consumers to the library.</summary>
internal sealed class KafkaNotificationMessageSource : INotificationMessageSource
{
    private readonly Channel<DeliveryRequest> channel;

    public KafkaNotificationMessageSource(IOptions<KafkaAdapterOptions> options)
    {
        channel = Channel.CreateBounded<DeliveryRequest>(
            new BoundedChannelOptions(options.Value.ChannelCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
            });
    }

    public async Task SubmitAsync(NotificationEnvelope notification, CancellationToken cancellationToken)
    {
        // Each item carries its own completion so concurrent Kafka workers can wait for the
        // exact notification they submitted without coupling to channel read order.
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await channel.Writer.WriteAsync(new DeliveryRequest(notification, completion), cancellationToken);
        await completion.Task.WaitAsync(cancellationToken);
    }

    public async Task RunAsync(
        Func<NotificationEnvelope, CancellationToken, Task> handler,
        CancellationToken cancellationToken)
    {
        await foreach (var request in channel.Reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                // The library handler represents local routing acceptance. WebSocket delivery
                // and application-level acknowledgement are separate, later boundaries.
                await handler(request.Notification, cancellationToken);
                request.Completion.TrySetResult();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                request.Completion.TrySetException(exception);
            }
        }
    }

    private sealed record DeliveryRequest(
        NotificationEnvelope Notification,
        TaskCompletionSource Completion);
}
