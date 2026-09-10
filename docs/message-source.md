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

The neutral envelope contains only direct `UserIds` and opaque `Subscriptions`. An adapter passes subscription strings through unchanged; the consuming application owns meanings and namespaces such as groups, events, roles, tenants, or partners.

## Multi-server fan-out

For cluster-wide publishing, every active WebSocket server must have an independent subscription to the shared source and receive every notification. Each server then resolves recipients only from its local in-memory registry. For Kafka, this requires an independent consumer group per simultaneously active server. A shared group load-balances records and can route a notification to a server with no matching local connection.

Kafka group identity follows `{application}.{environment}.{instance-id}`. The instance ID belongs to one active process incarnation, so a restart creates a new group. A previously unseen group starts at the live end and does not consume the offline interval; old ephemeral group metadata follows the broker's retention policy. This is intentional because V1 has no replay or offline inbox.

Equivalent provider topologies are application-owned. For example, RabbitMQ can use one queue per active server bound to a fan-out exchange. The core library neither configures those providers nor carries server identity in the notification envelope.

`WebSocketNotificationHub.PublishAsync` bypasses the source and is therefore local to its process. Use the shared source for cluster-wide delivery.

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

An active server must report source readiness only after it can receive new provider records. Because V1 has no replay or offline inbox, a restarted server must begin live consumption without replaying notifications emitted while it was offline. Exact provider offset/start-position mechanics remain the adapter's responsibility and must be documented and tested.

The RC.1 Kafka sample still uses one fixed group ID and does not yet expose source readiness; it is the known implementation gap addressed by the next V1 release stage.
