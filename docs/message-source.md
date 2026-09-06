# Message-Source Integration

`INotificationMessageSource` is the single queue-neutral boundary in the core library:

```csharp
public interface INotificationMessageSource
{
    Task RunAsync(
        Func<NotificationEnvelope, CancellationToken, Task> handler,
        CancellationToken cancellationToken);
}
```

The library accepts zero or one registered source. Zero allows direct publishing through `WebSocketNotificationHub`; more than one is rejected because combining independent sources would make ordering semantics ambiguous.

## Library responsibility

- Accept a neutral `NotificationEnvelope`
- Reject invalid/expired/oversized notifications as applicable
- Resolve current local recipients
- Serialize the client notification frame once
- Apply per-client bounded-buffer policy
- Own connection send/receive lifetime

## Adapter responsibility

- Connect to and consume from its provider
- Deserialize provider data into the neutral contract
- Select commit/ACK/NACK behavior
- Apply provider retry and poison-message policy
- Preserve the application's required source ordering
- Propagate host cancellation

The handler callback completes after local routing has accepted the notification into applicable bounded connection buffers. It does not wait for browser processing and is not a queue-independent ACK instruction. Provider adapters choose what callback completion means for their own offset/acknowledgement rules.

An individual connection's successful delivery is defined separately: its `WebSocket.SendAsync` completed. A later send failure ends that connection; V1 does not retry the notification.

## KafkaHighThroughput sample

The hosted sample implements a bounded adapter channel. `KafkaNotificationConsumer` deserializes the Kafka value using web JSON naming, converts it to `NotificationEnvelope`, and awaits `KafkaNotificationMessageSource.SubmitAsync`. The source worker awaits core routing before the adapter completes the Kafka handler.

The sample configures ordered-by-partition consumption. Kafka connection, retry, poison-message, and commit behavior belongs to KafkaHighThroughput and its sample configuration. No Kafka type exists in the core project.

## Other providers

A RabbitMQ adapter could deserialize a delivery, await the neutral handler, and then apply application-chosen ACK/NACK semantics. A ZeroMQ adapter could read a frame and call the same handler without any broker acknowledgement. Both live in the consuming application or separate provider packages; neither requires changing the WebSocket core or creating a universal broker framework.

## Cancellation and failure

`RunAsync` should remain active until its cancellation token is signaled. It must pass cancellation to provider I/O and the handler. Non-cancellation failures should escape the method so the .NET host observes the background-service failure. The sample's bounded bridge propagates handler failures back to the Kafka consumer callback.
