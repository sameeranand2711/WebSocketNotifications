# Configuration

The conventional section is `WebSocketNotifications`. Binding uses standard .NET options, so values may come from JSON, environment variables, user secrets, configuration services, or direct code.

| Option | Default | Valid values |
|---|---:|---|
| `EndpointPath` | `/ws/notifications` | Nonblank absolute path beginning with `/`, without `?` or `#` |
| `HeartbeatEnabled` | `true` | `true` or `false` |
| `HeartbeatInterval` | `00:00:30` | Greater than zero and at most 1 hour |
| `HeartbeatTimeout` | `00:00:10` | Greater than zero, at most 1 hour, and not greater than the interval |
| `CompressionEnabled` | `false` | `true` or `false` |
| `MaxIncomingMessageSize` | `65536` | 1 through 1,048,576 bytes |
| `MaxOutgoingMessageSize` | `262144` | 1 through 4,194,304 bytes |
| `OutgoingBufferCapacity` | `128` | 1 through 10,000 messages per connection |
| `MaxSubscriptionsPerConnection` | `128` | 1 through 10,000 unique keys per connection |
| `MaxSubscriptionKeyLength` | `256` | 1 through 4,096 characters |
| `SlowClientPolicy` | `Disconnect` | `Disconnect`, `DropOldest`, or `DropCurrent` |

The maximum sizes, capacities, subscription-key length, and one-hour heartbeat ceiling are hard safety limits. Invalid settings are rejected rather than clamped. Heartbeat duration settings remain validated even when heartbeat is disabled.

## Configuration binding

```csharp
using WebSocketNotifications.Configuration;
using WebSocketNotifications.Hosting;

builder.Services.AddWebSocketNotifications(
    builder.Configuration.GetSection(WebSocketNotificationOptions.SectionName));
```

```json
{
  "WebSocketNotifications": {
    "EndpointPath": "/ws/notifications",
    "HeartbeatEnabled": true,
    "HeartbeatInterval": "00:00:30",
    "HeartbeatTimeout": "00:00:10",
    "CompressionEnabled": false,
    "MaxIncomingMessageSize": 65536,
    "MaxOutgoingMessageSize": 262144,
    "OutgoingBufferCapacity": 128,
    "MaxSubscriptionsPerConnection": 128,
    "MaxSubscriptionKeyLength": 256,
    "SlowClientPolicy": "Disconnect"
  }
}
```

For environment variables, use normal .NET separators, for example:

```text
WebSocketNotifications__HeartbeatEnabled=false
WebSocketNotifications__OutgoingBufferCapacity=256
WebSocketNotifications__MaxSubscriptionsPerConnection=128
WebSocketNotifications__MaxSubscriptionKeyLength=256
```

## Programmatic options

```csharp
builder.Services.AddWebSocketNotifications(options =>
{
    options.EndpointPath = "/notifications";
    options.HeartbeatInterval = TimeSpan.FromSeconds(45);
    options.HeartbeatTimeout = TimeSpan.FromSeconds(15);
    options.MaxIncomingMessageSize = 128 * 1024;
    options.MaxOutgoingMessageSize = 512 * 1024;
    options.OutgoingBufferCapacity = 256;
    options.MaxSubscriptionsPerConnection = 128;
    options.MaxSubscriptionKeyLength = 256;
    options.SlowClientPolicy = SlowClientPolicy.DropCurrent;
});
```

Subscription additions are evaluated atomically against the per-connection limit. Existing duplicate keys do not consume quota, and unsubscribe or disconnect releases the associated state. There is no separate keys-per-command option: the inbound byte limit bounds parsing, while the subscription limit bounds every command that can grow state.

## Validation timing

Options are validated when the WebSocket subsystem is resolved. Mapping the endpoint resolves them immediately. A host that registers the optional subsystem but neither maps it nor registers a message source can still start without eagerly constructing it; using the invalid subsystem then produces an `OptionsValidationException` with all detected failures.

## Compression

When enabled, the endpoint requests WebSocket response compression. Compression is disabled by default because compressing attacker-controlled data together with secrets can create side channels. The application must decide whether its payload and authentication model make compression appropriate.

## Sample-specific settings

The host sample additionally uses these `KafkaAdapter` settings:

| Option | Default | Behavior |
|---|---:|---|
| `ChannelCapacity` | `256` | Bounded handoff capacity; valid range 1 through 10,000 |
| `ApplicationName` | `websocket-notifications` | Required application namespace in the consumer group |
| `InstanceId` | generated GUID | Optional explicit identity unique to this active process incarnation |

The effective group is `{application}.{environment}.{instance-id}`. The environment segment comes from the ASP.NET Core host environment. The sample overwrites the named Kafka consumer's `GroupId` with this value and enforces `AutoOffsetReset=Latest`, so an ephemeral restarted process does not replay notifications emitted while it was offline. Explicit `InstanceId` values must be unique among simultaneously active hosts; reusing one intentionally creates a shared Kafka group and breaks fan-out.

The sample exposes liveness at `/` and Kafka source readiness at `/health/ready`. Readiness returns HTTP 200 only after the consumer is running and has a partition assignment; degraded and unhealthy states return HTTP 503.

The remaining `KafkaConsumerWorkers` settings and the producer's `NotificationProducer` and `KafkaProducerClients` settings are sample/provider configuration, not core library options. Broker credentials should be supplied by environment variables or secret providers and must not be committed.
