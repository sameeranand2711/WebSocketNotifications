# Hosted Consumer Sample

This ASP.NET Core sample connects KafkaHighThroughput to the provider-neutral WebSocket notification library.

```text
Kafka record
  -> KafkaNotificationConsumer (JSON deserialization)
  -> bounded KafkaNotificationMessageSource bridge
  -> WebSocketNotificationHub
  -> connected clients
```

## Stable V1 multi-server requirement

Stable V1 uses source-level fan-out. Every simultaneously active host must consume the notification topic through an independent consumer group and then route only to its local WebSocket connections. A shared consumer group is invalid because it load-balances each Kafka record to one host.

Group identity must follow `{application}.{environment}.{instance-id}`, with the instance ID unique to one active process incarnation. A restarted process uses a new group configured to start at the live end, so it does not replay the offline interval. Old group metadata follows Kafka's retention policy. Server identity is deployment metadata and never appears in `NotificationEnvelope`.

The merged RC.1 sample does not implement this requirement yet: `appsettings.json` contains the fixed group `websocket-notification-host`, the root endpoint is liveness rather than source readiness, and only a one-host E2E has passed. Do not run multiple RC.1 hosts with the default group and assume fan-out. Stage 13 will implement and prove the required group lifecycle, readiness, no-replay restart behavior, and deterministic two-host E2E.

## Run

From the repository root:

```powershell
docker compose up -d --wait
docker compose exec -T kafka /opt/kafka/bin/kafka-topics.sh --bootstrap-server localhost:9092 --create --if-not-exists --topic notifications --partitions 3 --replication-factor 1
dotnet run --project samples/WebSocketNotifications.Host --urls http://localhost:5000
```

The Compose health check waits for Kafka's consumer-group coordinator before reporting the broker healthy.

The RC.1 liveness response is at `http://localhost:5000/`; it does not prove Kafka source readiness. Stable V1 must not report source readiness until the host can receive new notification records. The default WebSocket endpoint is:

```text
ws://localhost:5000/ws/notifications?userId=user-1
```

The query-string authentication handler exists only to make the local sample runnable. Production applications must configure a real ASP.NET Core authentication scheme and resolve identity from its authenticated principal/session.

## Application boundaries

- `ClaimUserResolver` reads the authenticated name-identifier claim.
- `SampleSubscriptionAuthorizer` permits every opaque subscription key for demonstration purposes.
- `SampleInboundMessageHandler` demonstrates application-specific inbound handling without logging payload data.
- `KafkaNotificationConsumer` owns Kafka JSON deserialization.
- `KafkaNotificationMessageSource` is a bounded handoff into the neutral source interface.

## Configuration

`appsettings.json` contains three independent areas:

- `WebSocketNotifications`: core endpoint, heartbeat, compression, size, buffer, and slow-client options
- `KafkaAdapter.ChannelCapacity`: bounded adapter handoff capacity
- `KafkaConsumerWorkers`: KafkaHighThroughput broker, topic, consumer identity, ordering, retry, shutdown, and poison-message settings

Override values through normal .NET configuration. For example:

```powershell
$env:KafkaConsumerWorkers__Consumers__0__BootstrapServers = 'broker:9092'
dotnet run --project samples/WebSocketNotifications.Host
```

Do not place broker credentials in committed settings. Use environment variables, user secrets, or an external secret provider.

The consumer uses `OrderedByPartition`; no global ordering is claimed across Kafka partitions.

`WebSocketNotificationHub.PublishAsync` is local to one host process. Publish through the shared source when every active host must receive the notification.

The Kafka notification JSON contains `userIds` and `subscriptions`. Subscription strings are passed through unchanged; applications own naming and authorization conventions, including tenant or partner prefixes.
