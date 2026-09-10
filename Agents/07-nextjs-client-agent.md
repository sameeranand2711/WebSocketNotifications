# React / Next.js WebSocket Client Agent


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

Run this agent only after the core library has passed its core stabilization quality gate and its V1 public API/protocol are stable enough for sample consumption.

Do not build the final Next.js sample during active core-library development.

The sample client should validate the stabilized consumer experience rather than constantly track an evolving protocol/API.

# Role

Create a minimal but production-sensible React/Next.js frontend that demonstrates how a browser client connects to the WebSocket notification server, subscribes, receives notifications, reconnects, and resubscribes.

This is a base client/sample, not a full UI product.

# Objectives

Build a Next.js application with reusable client-side WebSocket components/hooks.

The UI must demonstrate:
- authenticated WebSocket connection
- connection status
- reconnect behavior
- resubscription after reconnect
- generic opaque subscriptions
- unsubscribe
- received notification list
- protocol errors
- optional application-level message handling
- heartbeat compatibility as required by the server protocol

# Authentication

The library endpoint requires authentication.

Do not hard-code a production authentication strategy.

Provide a simple adapter boundary for acquiring authentication/session credentials from the host frontend.

If browser WebSocket authentication requires cookies/session state in the sample, use that approach cleanly.

Do not put long-lived secrets into source code.

# Suggested Structure

Use a structure similar to:

```text
src/
  app/
  components/
    notifications/
  hooks/
    useWebSocketNotifications.ts
  lib/
    websocket/
      client.ts
      protocol.ts
      types.ts
  providers/
    NotificationProvider.tsx
```

Adjust if the project conventions justify a better structure.


# Frontend Configuration

Do not hardcode environment-specific server URLs or authentication endpoints in reusable client code.

Use normal Next.js environment/configuration mechanisms for values such as:
- WebSocket server URL/base URL
- reconnect/backoff settings where exposed
- sample authentication endpoint/base URL if applicable

Provide an example environment file such as:

```text
.env.example
```

Do not commit secrets.

The client protocol constants themselves may remain fixed in code when they define the V1 protocol contract.

# Client API

Create a reusable client abstraction/hook with operations conceptually similar to:

```ts
connect()
disconnect()
subscribe(...)
unsubscribe(...)
sendApplicationMessage(...)
```

Expose state such as:
- connecting
- connected
- reconnecting
- disconnected
- lastError
- active subscriptions
- notifications

# Reconnect

V1 server does not replay missed notifications.

Client owns reconnect and resubscribe.

Implement:
- bounded exponential backoff with jitter
- cancellation when component/client intentionally disconnects
- resubscribe only after successful reconnect
- no duplicate reconnect loops

Document that notifications published while disconnected may be lost.

# Protocol

Use the actual protocol contract implemented by the library.

Do not invent unsupported commands.

Keep protocol types centralized.

Handle malformed/unknown server messages defensively.

# UI

Keep UI simple:
- connection state
- input for opaque subscription keys
- active subscription display
- notification stream
- clear notifications button
- connection/error log

No heavy design system is required.

# Tests

Add focused frontend tests where practical:
- protocol parsing
- reconnect state machine
- resubscribe after reconnect
- unsubscribe
- duplicate reconnect prevention

Do not over-test React rendering details.

# Deliverable

A runnable Next.js sample/base app plus README instructions for:
- install
- run
- configure server URL
- authentication assumptions
- expected protocol
