# WebSocket Notifications

WebSocket Notifications is a provider-neutral .NET library for routing JSON notifications from an application-owned message source to authenticated ASP.NET Core WebSocket clients. Stable V1 uses source-level fan-out across WebSocket servers while keeping connection and subscription state local to each process.

## V1 capabilities

- Authenticated, configurable ASP.NET Core WebSocket endpoint
- Application-defined user resolution and subscription authorization
- Direct-user delivery to every active connection for that user
- Opaque application-defined subscription keys
- Programmatic subscription management through `WebSocketNotificationHub`
- Provider-neutral `INotificationMessageSource` boundary
- Multi-server source-level fan-out: every active server independently receives each cluster-wide notification and performs local routing
- Bounded per-connection buffers with disconnect, drop-oldest, and drop-current policies
- One send loop and one receive loop per connection
- Application-level heartbeat, optional WebSocket compression, and message-size limits
- JSON application payloads and application-specific inbound messages
- UTC notification expiry and per-connection ordering
- KafkaHighThroughput host and multi-endpoint producer Web API samples
- Reconnecting Next.js client with automatic resubscription

V1 intentionally does not provide distributed presence or targeted server resolution, replay, durable offline delivery, built-in acknowledgements, WebSocket retries, exactly-once delivery, binary messages, tenant scopes, subscription TTLs, or multi-region routing. See [V1 limitations](docs/limitations.md).

## Architecture

```text
Shared application message source
        |
        +-- WebSocket server A -> local routing -> local clients
        +-- WebSocket server B -> local routing -> local clients
        +-- WebSocket server C -> local routing -> local clients
```

Each active server must have an independent source subscription. For Kafka, simultaneously active servers must use independent consumer groups; members of one shared group load-balance records and do not provide fan-out. The hosted sample generates `{application}.{environment}.{instance-id}`, uses a new process identity when `InstanceId` is omitted, starts unseen groups at the live end, and exposes assignment-aware readiness at `/health/ready`. The core package contains no Kafka, RabbitMQ, Redis, or other broker dependency. Provider-specific fan-out, deserialization, acknowledgement, retry, and commit behavior remains in the consuming application. See [architecture](docs/architecture.md) and [message sources](docs/message-source.md).

## Requirements

- .NET 8 SDK or a newer SDK capable of targeting .NET 8
- Node.js 20.9 or newer for the Next.js sample
- Docker Desktop for the Kafka sample and live E2E test

The repository pins SDK `10.0.101` for repeatable local builds. The library itself targets `net8.0`.

## Installation

After packing locally:

```powershell
dotnet pack src/WebSocketNotifications/WebSocketNotifications.csproj -c Release -o artifacts/packages
dotnet add <your-project> package WebSocketNotifications --version 1.0.0-rc.1 --source artifacts/packages
```

During repository development, use a project reference:

```xml
<ProjectReference Include="../../src/WebSocketNotifications/WebSocketNotifications.csproj" />
```

## ASP.NET Core setup

Register the core services, application boundaries, and configured endpoint:

```csharp
using WebSocketNotifications.Abstractions;
using WebSocketNotifications.Configuration;
using WebSocketNotifications.Hosting;

builder.Services.AddAuthentication(/* application scheme */);
builder.Services.AddAuthorization();
builder.Services.AddSingleton<IWebSocketUserResolver, ApplicationUserResolver>();
builder.Services.AddSingleton<ISubscriptionAuthorizer, ApplicationSubscriptionAuthorizer>();
builder.Services.AddWebSocketNotifications(
    builder.Configuration.GetSection(WebSocketNotificationOptions.SectionName));

var app = builder.Build();
app.UseWebSockets();
app.UseAuthentication();
app.UseAuthorization();
app.MapWebSocketNotifications();
```

Programmatic configuration is also supported:

```csharp
builder.Services.AddWebSocketNotifications(options =>
{
    options.EndpointPath = "/ws/notifications";
    options.OutgoingBufferCapacity = 256;
    options.SlowClientPolicy = SlowClientPolicy.Disconnect;
});
```

The library uses standard .NET options and does not require `appsettings.json`. Any `IConfiguration` provider can supply settings.

## Authentication and identity

`MapWebSocketNotifications()` applies `RequireAuthorization()`. The application configures its own authentication scheme; the library contains no JWT- or cookie-specific validation.

After authentication, `IWebSocketUserResolver` derives the direct-routing user ID from the request. Clients cannot add values to `UserIds`; the subscription protocol only manages opaque application-defined keys. The sample's query-string identity is deliberately local-demo-only and must not be copied into production authentication.

`ISubscriptionAuthorizer` is invoked before every client subscribe request. The default implementation denies all subscriptions, so an application must opt in to the subscriptions it accepts.

The library does not parse or assign meaning to subscription keys. Groups, feeds, events, roles, tenants, partners, channels, and markets are application concepts. Applications namespace and authorize keys as needed, for example `tenant:abc:group:premium` or `partner:p1:event:deposit.completed`. Direct `UserIds` remain separate because they are bound to authenticated identity rather than client-controlled subscriptions.

## Notification contract

The application or message-source adapter constructs the neutral contract:

```csharp
using System.Text.Json;
using WebSocketNotifications.Contracts;

using var payload = JsonDocument.Parse("""{"score":7}""");
var notification = new NotificationEnvelope(
    messageId: "score-42",
    payload: payload.RootElement,
    createdAt: DateTimeOffset.UtcNow,
    expiresAt: DateTimeOffset.UtcNow.AddMinutes(1),
    userIds: ["user-123"],
    subscriptions: ["group:operators", "feed:match-42", "event:score.changed"]);
```

At least one routing target is required. Timestamps must use UTC offset zero. A connection matching several routes receives one copy of the notification.

## Message-source integration

An application may register zero or one source:

```csharp
using WebSocketNotifications.Abstractions;
using WebSocketNotifications.Contracts;

public sealed class ApplicationNotificationSource : INotificationMessageSource
{
    public async Task RunAsync(
        Func<NotificationEnvelope, CancellationToken, Task> handler,
        CancellationToken cancellationToken)
    {
        await foreach (var notification in ReadNotifications(cancellationToken))
        {
            await handler(notification, cancellationToken);
        }
    }
}

builder.Services.AddSingleton<INotificationMessageSource, ApplicationNotificationSource>();
```

The callback completion means the notification has been validated, routed, and accepted by the applicable bounded connection buffers. It is not a client acknowledgement. An individual WebSocket delivery is successful only after that connection's send operation completes. See [delivery semantics](docs/delivery-semantics.md).

Applications can also inject `WebSocketNotificationHub` and call `PublishAsync` directly. That method intentionally routes only within the current process. Applications requiring cluster-wide delivery must publish through the shared fan-out source.

## Client protocol

Subscribe and unsubscribe requests carry one or more opaque keys:

```json
{"type":"subscribe","requestId":"request-1","subscriptions":["group:operators","feed:match-42"]}
```

```json
{"type":"unsubscribe","requestId":"request-2","subscriptions":["group:operators"]}
```

Notifications use this server frame:

```json
{
  "type": "notification",
  "messageId": "score-42",
  "payload": { "score": 7 },
  "createdAt": "2026-09-06T09:30:00Z",
  "expiresAt": "2026-09-06T09:31:00Z"
}
```

See [protocol reference](docs/protocol.md) for confirmations, errors, heartbeat messages, close behavior, and application-specific inbound messages.

## Configuration

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
    "SlowClientPolicy": "Disconnect"
  }
}
```

Invalid settings are rejected when the WebSocket subsystem is resolved or mapped; values are never silently clamped. See [configuration reference](docs/configuration.md).

## Slow clients and heartbeat

Every connection owns a bounded outgoing buffer. On overflow:

- `Disconnect` removes and stops the slow connection; this is the default.
- `DropOldest` keeps the newest message by discarding the oldest queued message.
- `DropCurrent` discards the message currently being routed.

Routing uses non-blocking buffer writes, so one full client queue cannot block other clients.

Heartbeat is enabled by default. The server sends `ping` frames containing a nonce and expects a matching `pong` before the configured timeout. When disabled, dead connections are detected only by normal read, write, or close failures.

## Delivery and ordering

V1 delivery success for a connection means its WebSocket send operation completed. It does not prove browser receipt, application processing, or acknowledgement. There is no WebSocket retry or replay, and upstream at-least-once systems may produce duplicates.

The source controls its own ordering domain. The library routes notifications synchronously into connection buffers, and each connection has one FIFO send loop. The Kafka producer sample selects an application-owned key such as `user:user-123`; the core never creates Kafka keys. There is no order across unrelated partitions or keys. See [delivery semantics](docs/delivery-semantics.md) and [ordering](docs/ordering.md).

## Run the samples

Start Kafka and create the topic:

```powershell
docker compose up -d --wait
docker compose exec -T kafka /opt/kafka/bin/kafka-topics.sh --bootstrap-server localhost:9092 --create --if-not-exists --topic notifications --partitions 3 --replication-factor 1
```

The Compose health check waits for Kafka's consumer-group coordinator, so `--wait` does not return while direct-user delivery is still unavailable during broker startup.

Run the host in one terminal:

```powershell
dotnet run --project samples/WebSocketNotifications.Host --urls http://localhost:5000
```

Run the Next.js client in another:

```powershell
cd samples/clients/websocket-notifications-nextjs
Copy-Item .env.example .env.local
npm install
npm run dev
```

Open `http://localhost:3000`; the local sample defaults to user `user-1`. Start the producer API in another terminal:

```powershell
dotnet run --project samples/NotificationProducer --urls http://localhost:5001
```

Open `http://localhost:5001/swagger` to inspect and invoke the producer endpoints through Swagger UI.

Then publish a direct-user notification:

```powershell
$body = @{ users = @('user-1'); payload = @{ score = 7 } } | ConvertTo-Json -Depth 4
Invoke-RestMethod -Method Post -Uri http://localhost:5001/api/notifications/users -ContentType application/json -Body $body
```

For a fully automated real-broker check:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-multi-host-e2e.ps1
```

The multi-host runner proves the shared-group failure mode, independent-group fan-out, continued delivery after one host stops, and no replay after restart. It uses a unique topic and cleans up its processes, topic, and Kafka container by default.

Additional details are in the [host sample](samples/WebSocketNotifications.Host/README.md), [producer sample](samples/NotificationProducer/README.md), and [Next.js client](samples/clients/websocket-notifications-nextjs/README.md).

## Tests and development

```powershell
dotnet test WebSocketNotifications.slnx --configuration Release
cd samples/clients/websocket-notifications-nextjs
npm test
npm run typecheck
npm run build
```

The repository contains one core library, one behavior-oriented xUnit project, two .NET samples, and one Next.js client. Read [testing](docs/testing.md) and [development](docs/development.md) before contributing.

## Reference documentation

- [Architecture](docs/architecture.md)
- [WebSocket protocol](docs/protocol.md)
- [Configuration](docs/configuration.md)
- [Message-source boundary](docs/message-source.md)
- [Delivery semantics](docs/delivery-semantics.md)
- [Ordering](docs/ordering.md)
- [Testing](docs/testing.md)
- [Development](docs/development.md)
- [V1 limitations](docs/limitations.md)
- [Decision log](docs/decision-log.md)
- [Changelog](CHANGELOG.md)
- [Release notes](RELEASE_NOTES.md)

## Roadmap

Potential post-V1 work includes distributed presence and targeted per-server inboxes as an alternative to all-node fan-out, tenant-aware scopes, replay or durable offline storage, optional application acknowledgement helpers, binary protocol negotiation, subscription TTLs, and multi-region routing. These are roadmap items, not current capabilities.
