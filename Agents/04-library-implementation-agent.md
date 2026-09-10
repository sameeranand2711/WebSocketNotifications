# WebSocket Notification Library Implementation Agent


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


# Role

You implement the production library against tests already written by the TDD Test Agent.


# Agent Cadence and Governing Rules

Assume `02-development-rules-agent.md` has already been read and its rules remain active.

Do not require a full Development Rules review after every small implementation slice.

For each TDD slice:
- implement
- run focused tests
- run affected suite
- refactor while green
- continue

A full rules review happens at the end of the major phase unless this implementation introduces a material architecture/public API change that needs immediate review.

# Mandatory Development Rules

Before implementing or refactoring, apply `02-development-rules-agent.md`.

In particular:

- prefer concrete classes by default
- create interfaces only at genuine application/provider boundaries
- do not introduce a design pattern unless direct code is worse
- do not create layers that only forward method calls
- do not split simple behavior across many tiny services
- keep the public API small
- if two designs satisfy the tests, choose the one with fewer concepts and less indirection
- when a previously introduced abstraction becomes unnecessary, remove it

A solution is not considered complete merely because it compiles and passes tests. It must also remain easy to read and debug.

# Golden Rule

Do not implement speculative features.

Implement only enough production behavior to make the current failing tests pass while preserving architectural quality.

# Development Workflow

For each assigned failing test:

1. Read the test and surrounding production code.
2. Confirm the test reflects the documented requirements.
3. Run the test and observe the failure.
4. Implement the smallest coherent production change.
5. Run the focused test.
6. Run the affected test project.
7. Run the entire solution test suite.
8. Refactor only while green.
9. Report what changed and any architectural concern discovered.


# Sample Application Boundary

Do NOT create or depend on the final sample applications while the core library is still under active TDD development.

Use only tests and test hosts to validate the library.

The hosted consumer, producer, and Next.js samples are created after the core library:
- implements all agreed V1 behavior
- passes the core test suite
- passes the core quality gate
- has a stabilized V1 public API

If later sample development reveals a real library defect, reproduce it first with a library test and then fix the library through TDD.

# Required Architecture

Keep the code conceptually separated into a small number of real responsibilities.

Potential responsibilities include:
- public options/configuration
- WebSocket endpoint/middleware
- connection lifecycle
- connection registry
- subscription registry/manager
- subscription authorization
- identity resolution
- notification routing
- per-connection outbound buffer
- connection send loop
- connection receive loop
- heartbeat
- neutral notification envelope
- protocol messages
- message-source abstraction
- application inbound-message callback
- logging

Do NOT blindly create one project or interface per responsibility. Start simple and split only where dependencies/ownership justify it.


# Configuration Implementation Rules

Do not scatter operational values through production code.

Expose a cohesive options model for settings the consuming application may reasonably need to change.

Use standard .NET Options/configuration integration.

Support:
- configuration binding from an `IConfigurationSection`
- programmatic options configuration

Do not require the core library to know about `appsettings.json`.

The sample host will use `appsettings.json`, but the core library must work with any standard .NET configuration provider.

Keep true protocol invariants and absolute internal safety ceilings in code when they should not be user-configurable.

Do not silently clamp unsafe settings.

Validate configured values and report invalid settings clearly.

Prefer one coherent options tree over many unrelated option classes unless separate option objects genuinely improve clarity.

# Message Source Boundary

The core library MUST define a provider-neutral source/listener abstraction.

The core library MUST NOT reference:
- KafkaHighThroughput
- Confluent.Kafka
- RabbitMQ.Client
- NetMQ
- ZeroMQ packages

Provider-specific acknowledgement/commit/retry logic belongs to the source implementation supplied by the host application or a future adapter package.

Handler success/failure must be observable by the source so the source can apply provider-specific semantics.

# WebSocket Ownership

The library owns the WebSocket after successful upgrade.

For each connection:
- maintain a receive loop
- maintain exactly one logical send loop
- use a bounded outbound channel/buffer
- ensure network send operations are serialized
- surface application-specific inbound messages without handing ownership of the raw socket to application code

# Identity

Do not implement JWT parsing.

Use ASP.NET Core authentication state and an application-configurable user resolver.

The direct-user routing identity is established during connection creation and cannot be changed through a subscription command.

# Subscription Authorization

Provide a pluggable application authorization boundary for opaque subscription keys.

Keep this independent of authentication.

# Routing

Routing must support:
- UserIds
- Subscriptions
- combinations

Preserve the current V1 duplicate-routing decision. Do not introduce deduplication unless tests/requirements explicitly change.

# Expiration

`ExpiresAt` is optional UTC time.

Expired notifications must be discarded before unnecessary routing/enqueue work.

# Slow Clients

Use bounded per-connection buffers.

Default overflow policy: disconnect.

Support configured policies required by tests.

Never wait indefinitely on a full client buffer from the shared routing path.

# Heartbeat

Enabled by default and configurable.

When enabled:
- proactively identify dead clients using heartbeat timeout

When disabled:
- do not invent an idle timeout
- rely on close/read/write failures for passive detection

# Message Limits

Expose separate incoming/outgoing configurable limits.

Also enforce library hard safety bounds.

Do not silently clamp dangerous configuration values.

# Ordering

Preserve enqueue/send ordering for a single connection.

Do not promise global ordering across independent source streams.

Never use concurrent writes on the same WebSocket.

# Configuration

Use .NET options/configuration patterns.

The default endpoint is `/ws/notifications`.

Design invalid WebSocket subsystem configuration reporting carefully. The host application should not accidentally be terminated solely because an optional subsystem setting is bad unless the public contract explicitly chooses fail-fast for that setting.

# Performance Constraints

Avoid:
- repeated large payload copies
- per-message reflection where avoidable
- lock contention on global state
- unbounded allocations
- one Task.Run per notification
- one thread per connection
- blocking waits
- LINQ-heavy hot paths when a simple loop is clearer and faster

Prefer correctness and clarity first; optimize only measurable hot paths.

# Deliverables

Maintain:
- buildable library
- passing tests
- XML documentation for public API where useful
- sample minimal registration/use if a sample project exists

At the end of each cycle report:
- files changed
- tests made green
- design decisions introduced
- deferred concerns
- exact commands used to build/test
