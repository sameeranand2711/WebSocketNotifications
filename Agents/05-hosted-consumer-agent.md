# Hosted Notification Consumer Agent


# Shared Project Context

We are building a reusable .NET WebSocket notification library.

## Core Goals

The library reads notification messages from an abstract message source and routes them to connected WebSocket clients.

The core library MUST NOT depend directly on Kafka, RabbitMQ, ZeroMQ, or any specific queue/broker implementation.

A consuming application provides an implementation of a message-source abstraction, conceptually similar to:

```csharp
public interface INotificationMessageSource
{
    Task RunAsync(
        Func<NotificationEnvelope, CancellationToken, Task> handler,
        CancellationToken cancellationToken);
}
```

The exact API may evolve during implementation if tests demonstrate a better design, but provider-specific acknowledgement, commit, retry, and deserialization semantics MUST remain outside the WebSocket core.

## V1 Functional Requirements

- ASP.NET Core WebSocket endpoint.
- Default endpoint: `/ws/notifications`.
- Endpoint should be configurable.
- Endpoint requires authentication.
- Guest/anonymous access is the responsibility of the consuming application, which may authenticate guest identities.
- The library must not contain JWT-specific validation logic.
- User identity must be resolved through a pluggable application-provided resolver.
- A client MUST NOT be allowed to subscribe directly as another user.
- Direct-user routing is derived from the authenticated user identity.
- One authenticated user may have multiple active WebSocket connections; all active connections should receive direct notifications for that user.
- Client subscriptions use generic application-defined subscription keys.
- A client may subscribe to multiple opaque subscription keys simultaneously.
- Application code must be able to subscribe/unsubscribe one or more clients programmatically.
- Subscription authorization must be pluggable and controlled by the consuming application.
- No subscription TTL in V1.
- Subscriptions live until explicit unsubscribe, application removal, or connection close.
- Tenant/scope support is deferred, but the design must not prevent adding it later.
- JSON text protocol in V1.
- Application payload serialization and library control-protocol serialization are separate concerns.
- The consuming application owns source-message deserialization.
- V1 delivery success means the WebSocket send completed successfully.
- The library does NOT claim that the client processed or acknowledged the notification.
- Client acknowledgements are an application concern in V1.
- No WebSocket-level delivery retry in V1.
- Queue/source retry behavior belongs to the message-source implementation.
- Notification messages may contain optional UTC `ExpiresAt`.
- Expired notifications must not be routed/sent.
- Reconnect and resubscribe are client responsibilities.
- No replay in V1.
- Heartbeat enabled by default but configurable.
- Heartbeat failure means inactive/dead connection when heartbeats are enabled.
- If heartbeat is disabled, only passive failure detection is available through socket close/read/write failures.
- Compression is configurable and disabled by default.
- Separate configurable incoming and outgoing message-size limits.
- The library must enforce internal hard upper bounds so consumers cannot configure unsafe message sizes.
- Each WebSocket connection has a bounded outgoing buffer.
- Slow-client policy is configurable.
- Default slow-client policy: disconnect.
- Candidate policies:
  - Disconnect
  - DropOldest
  - DropCurrent/DropNewest
- The queue/message-source consumption path must never be indefinitely blocked by one slow WebSocket client.
- One send loop per connection.
- One receive loop per connection.
- The receive loop must handle library protocol messages and allow application-specific inbound messages to be surfaced through a callback/handler.
- V1 supports multiple WebSocket server instances through source-level fan-out.
- Every active WebSocket server independently receives every cluster-wide notification and performs only local routing.
- For Kafka, simultaneously active WebSocket servers use independent consumer groups.
- Connection and subscription tracking remain process-local and in-memory.
- Distributed presence and targeted server resolution are NOT implemented in V1.
- `WebSocketNotificationHub.PublishAsync` remains process-local; cluster-wide delivery uses the shared source.
- Restarted servers do not replay notifications emitted while they were offline.
- Do NOT put WebSocket server/instance identifiers into the neutral notification message contract.
- Ordering is expected.
- Ordering should be preserved from the message source through routing and per-connection sending.
- For Kafka-backed applications, the producing application may use Kafka keys to define the ordering domain; the WebSocket library must not generate Kafka keys itself.
- No global ordering guarantee across independent source partitions/keys/streams.

## Neutral Notification Model

The final model should be neutral to queue technology and should conceptually support:

```text
MessageId
UserIds[]
Subscriptions[]
Payload
CreatedAt
ExpiresAt?
```

At least one routing dimension must be present where appropriate.

The exact representation of `Payload` must be chosen carefully because:
- source deserialization is application-owned
- V1 WebSocket protocol is JSON text
- avoid repeated serialization/copying on the hot path where practical

## Engineering Principles

- TDD for core behavior.
- Keep code simple, readable, and easy to debug.
- Avoid unnecessary abstraction layers.
- Do not create interfaces merely for stylistic reasons.
- Use abstractions only at real architectural boundaries or where tests/extensibility justify them.
- Prefer composition over inheritance.
- Use async/await correctly.
- Avoid sync-over-async.
- Avoid unbounded queues/channels.
- Avoid fire-and-forget tasks unless lifetime and exception handling are explicit.
- Support cancellation throughout.
- Dispose resources correctly.
- Avoid holding global locks during network I/O.
- Avoid a single slow client affecting unrelated clients.
- Preserve source ordering where the source provides ordering.
- Avoid hidden background failures.
- Use structured logging through `ILogger`.
- Configuration should use standard .NET configuration/options patterns.
- Invalid WebSocket subsystem configuration should be surfaced clearly and should not silently clamp dangerous values.
- The broader host application should not necessarily crash solely because optional WebSocket configuration is invalid; design this behavior carefully and test it.
- The codebase should be production-oriented, not demo-quality.

## Testing Principles

Tests should cover:
- connection lifecycle
- user-to-connection mapping
- multiple connections per user
- subscribe/unsubscribe
- programmatic subscription management
- authorization
- direct-user routing
- generic subscription routing
- duplicate route matches
- expired messages
- outgoing buffer behavior
- each slow-client policy
- ordering preservation
- send failures
- receive failures
- heartbeat behavior
- heartbeat disabled behavior
- message-size limits
- configuration validation
- cancellation/shutdown
- application-specific inbound messages
- message-source success/failure propagation
- concurrency/race conditions where practical



# Prerequisite — Do Not Run Early

This agent MUST NOT run during active core-library development.

Run it only after the orchestration agent has declared:

```text
CORE LIBRARY STABILIZED
```

Prerequisites:
- all agreed V1 core functionality is implemented
- core-library tests pass
- core quality benchmark passes
- core mandatory gates pass
- the V1 public API is stabilized

This sample is intentionally created late to avoid repeatedly rewriting it as the library API evolves.

If this sample exposes a library problem, do not work around it silently in the sample. Reproduce the issue with a core/integration test first, then fix the library through the TDD cycle.

# Role

Create the ASP.NET Core hosted/sample application that:
1. hosts the WebSocket notification endpoint
2. implements the neutral message-source abstraction
3. consumes notification messages from the configured queue
4. passes neutral NotificationEnvelope instances into the WebSocket notification library

# Critical Boundary

The WebSocket library MUST NOT know about Kafka.

This hosted application/adapter layer may reference KafkaHighThroughput.

Conceptually:

```text
Kafka
  ↓
KafkaHighThroughput
  ↓
KafkaNotificationMessageSource
  ↓
INotificationMessageSource
  ↓
WebSocket Notification Library
  ↓
Connected clients
```


# Sample Configuration Requirements

This sample application MUST be configuration-driven.

Provide:
- `appsettings.json`
- `appsettings.Development.json` where development overrides are useful

Configuration should include the corresponding WebSocket notification settings and queue-adapter settings used by the sample.

Do not hardcode queue endpoints, topic names, consumer group names, heartbeat values, WebSocket endpoint paths, buffer capacities, or similar operational settings in `Program.cs`.

Keep WebSocket core settings under a dedicated section such as:

```json
{
  "WebSocketNotifications": {
    "Endpoint": "/ws/notifications"
  }
}
```

Keep Kafka/sample-adapter settings under a separate provider-specific section such as:

```json
{
  "Kafka": {
    "BootstrapServers": "localhost:9092",
    "Topic": "notifications",
    "ConsumerGroupPrefix": "websocket-notifications-development"
  }
}
```

The WebSocket core MUST NOT read or understand the Kafka section.

Do not commit real credentials/secrets.

Use user-secrets/environment variables for secrets when needed.

# Kafka Adapter

Implement a Kafka-backed `INotificationMessageSource` (or the actual final interface name) in this host/sample or a clearly separate adapter project.

Responsibilities:
- consume using KafkaHighThroughput
- deserialize queue message into neutral NotificationEnvelope
- invoke WebSocket library handler
- treat handler completion/failure according to KafkaHighThroughput semantics
- preserve ordered processing when configured
- propagate cancellation
- log source-specific failures

Do not leak Kafka types into WebSocket core.

# Host Responsibilities

Configure:
- authentication
- guest identity example if useful
- user-id resolver
- subscription authorizer
- WebSocket options
- message source
- inbound application-message handler

Provide a minimal authentication mechanism suitable for local development, clearly labeled as sample-only if it is not production hardened.

# V1 Multi-Server Topology

Every active WebSocket server independently consumes every cluster-wide notification and performs local in-memory routing. Kafka-backed hosts therefore require independent consumer groups, namespaced by application/environment and active server instance.

Do NOT add Redis, distributed connection tracking, targeted server resolution, or server identity to the notification envelope.

# End-to-End Verification

The host must support an end-to-end scenario:

```text
Producer
  ↓
Queue
  ↓
Hosted consumer
  ↓
Message source adapter
  ↓
WebSocket library
  ↓
Next.js client
```

Verify:
- direct user
- generic subscriptions
- expiry
- reconnect/resubscribe
- slow-client behavior where practical
- ordering for an explicitly keyed ordered stream

# Deliverables

- runnable hosted ASP.NET Core sample
- Kafka adapter
- local configuration example
- launch instructions
- authentication notes
- end-to-end validation instructions
