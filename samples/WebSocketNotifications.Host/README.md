# Hosted Consumer Sample

This ASP.NET Core sample connects KafkaHighThroughput to the provider-neutral WebSocket notification library.

```text
Kafka record
  -> KafkaNotificationConsumer (JSON deserialization)
  -> bounded KafkaNotificationMessageSource bridge
  -> WebSocketNotificationHub
  -> connected clients
```

## Run

From the repository root:

```powershell
docker compose up -d --wait
docker compose exec -T kafka /opt/kafka/bin/kafka-topics.sh --bootstrap-server localhost:9092 --create --if-not-exists --topic notifications --partitions 3 --replication-factor 1
dotnet run --project samples/WebSocketNotifications.Host --urls http://localhost:5000
```

The health response is at `http://localhost:5000/`. The default WebSocket endpoint is:

```text
ws://localhost:5000/ws/notifications?userId=user-1
```

The query-string authentication handler exists only to make the local sample runnable. Production applications must configure a real ASP.NET Core authentication scheme and resolve identity from its authenticated principal/session.

## Application boundaries

- `ClaimUserResolver` reads the authenticated name-identifier claim.
- `SampleSubscriptionAuthorizer` permits sample group/feed/event subscriptions.
- `SampleInboundMessageHandler` demonstrates application-specific inbound handling without logging payload data.
- `KafkaNotificationConsumer` owns Kafka JSON deserialization.
- `KafkaNotificationMessageSource` is a bounded handoff into the neutral source interface.

## Configuration

`appsettings.json` contains three independent areas:

- `WebSocketNotifications`: core endpoint, heartbeat, compression, size, buffer, and slow-client options
- `KafkaAdapter.ChannelCapacity`: bounded adapter handoff capacity
- `KafkaConsumerWorkers`: KafkaHighThroughput broker, topic, group, ordering, retry, shutdown, and poison-message settings

Override values through normal .NET configuration. For example:

```powershell
$env:KafkaConsumerWorkers__Consumers__0__BootstrapServers = 'broker:9092'
dotnet run --project samples/WebSocketNotifications.Host
```

Do not place broker credentials in committed settings. Use environment variables, user secrets, or an external secret provider.

The consumer uses `OrderedByPartition`; no global ordering is claimed across Kafka partitions.
