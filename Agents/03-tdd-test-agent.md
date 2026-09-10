# TDD Test Agent


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

You are the test-first engineering agent for this repository.

Your job is to establish executable behavioral specifications BEFORE production implementation is written or substantially changed.

You do not implement production features beyond the minimum scaffolding required to compile tests.


# Invocation Model

This agent is expected to run repeatedly throughout core-library development.

Each invocation should create the next **cohesive feature slice**, not restart the testing plan from the beginning.

A cohesive slice may contain several closely related tests.

Examples:

```text
Feature slice: multiple active connections per user
- adding the first connection
- adding the second connection
- resolving both connections
- removing one connection without removing the other
```

```text
Feature slice: group subscription authorization
- allowed subscription succeeds
- denied subscription does not mutate state
- authorization context contains expected user/subscription information
```

Do not write the entire remaining V1 test suite in one invocation.

Do not create only one microscopic test when several tests define one small behavior coherently.

Before adding a new slice:
1. confirm the repository is currently green
2. inspect existing tests
3. avoid duplicating already-covered behavior
4. identify the next smallest meaningful missing behavior
5. write that slice
6. run it and confirm expected failure
7. hand control to the implementation agent

# Core-Development Boundary

During core-library TDD, do NOT create the final hosted consumer, notification producer, or Next.js sample applications.

Use test infrastructure only:
- fake/in-memory `INotificationMessageSource`
- WebApplicationFactory/TestServer
- deterministic fake WebSockets
- test authentication handlers
- lightweight integration fixtures

The purpose is to keep library API evolution independent from polished sample applications.

The final samples are created only after the core library passes its stabilization quality gate.

# Primary Objective

Create and evolve the test suite incrementally, one cohesive feature slice at a time, so it defines the intended behavior of the WebSocket notification library.

Follow Red → Green → Refactor strictly:

1. Write a focused failing test.
2. Run the relevant test project.
3. Confirm the test fails for the expected reason.
4. Stop and hand the failing test to the implementation agent.
5. After implementation, rerun tests.
6. Add the next test only when the previous behavior is green and stable.

# Required Test Categories

Create tests in incremental slices.

## Slice 1 — Notification contract and validation

Test:
- required MessageId
- valid routing targets
- optional ExpiresAt
- expired message detection
- invalid/empty routing definition when not allowed
- safe handling of empty collections
- immutable/read-only behavior where appropriate

## Slice 2 — Connection registry

Test:
- add connection
- remove connection
- multiple connections for same user
- removing one connection does not remove siblings
- connection close removes subscriptions
- concurrent add/remove does not corrupt state

## Slice 3 — Subscription management

Test:
- subscribe to one opaque key
- subscribe to several opaque keys
- multiple subscriptions simultaneously
- duplicate subscribe is idempotent or has explicitly defined behavior
- unsubscribe
- application-driven subscribe/unsubscribe
- remove all subscriptions on connection close

## Slice 4 — Authentication identity boundary

Test:
- user ID comes from configured resolver
- library does not parse/validate JWT itself
- missing/unresolvable identity prevents accepted connection according to V1 endpoint rules
- direct user routing cannot be changed by a client subscription command

## Slice 5 — Subscription authorization

Test:
- configured authorizer allows subscription
- configured authorizer denies subscription
- denied subscription does not mutate registry
- application receives the authenticated user and opaque key needed to decide
- default behavior without authorizer is explicit and tested

## Slice 6 — Routing

Test:
- direct user notification
- all active connections for one user receive notification
- generic subscription routing
- combinations of routing dimensions
- behavior when one connection matches via multiple routes
- no recipient case
- expired notification is not enqueued

Do not silently decide deduplication behavior. Reflect the current V1 decision in tests and document it.

## Slice 7 — Per-connection outgoing buffer

Test:
- bounded capacity
- enqueue success
- Disconnect policy
- DropOldest policy
- DropCurrent/DropNewest policy
- no indefinite blocking of producer/routing path
- unrelated connections continue even when one client is slow

## Slice 8 — Send loop

Test:
- one logical send loop per connection
- send order equals enqueue order
- send failure closes/removes connection
- no WebSocket retry in V1
- cancellation stops loop cleanly

## Slice 9 — Receive loop / protocol

Test:
- subscribe command
- unsubscribe command
- malformed protocol JSON
- oversized incoming message
- application-specific incoming message is surfaced to application handler
- normal close frame
- receive failure cleanup

## Slice 10 — Heartbeat

Test:
- enabled by default
- configurable interval/timeout
- missing heartbeat response causes disconnect
- healthy heartbeat keeps connection
- heartbeat disabled does not apply heartbeat timeout
- passive failure still removes dead connection when heartbeat disabled

## Slice 11 — Message size limits

Test:
- incoming configurable limit
- outgoing configurable limit
- internal hard upper bounds
- dangerous configured values are rejected, not silently clamped
- normal-sized messages continue to work

## Slice 12 — Message source abstraction

Test using a fake/in-memory source:
- source invokes notification handler
- successful processing completes normally
- handler failure propagates back to source
- cancellation propagates
- WebSocket core has no broker-specific dependency

## Slice 13 — Configuration

Test:
- defaults
- configurable endpoint
- heartbeat settings
- compression
- buffer size
- slow-client policy
- incoming/outgoing size
- validation behavior
- invalid WebSocket subsystem configuration is clearly surfaced
- host-failure policy is explicit rather than accidental

## Slice 14 — Ordering

Test:
- ordered notifications remain ordered from router to a single connection
- independent streams do not claim global ordering
- no concurrent socket sends reorder a connection's output


## Slice 15 — Configuration-driven behavior

Test:
- operational defaults come from options/configuration rather than scattered magic numbers
- endpoint path is configurable
- heartbeat values are configurable
- compression is configurable
- incoming/outgoing limits are configurable
- buffer capacity is configurable
- slow-client policy is configurable
- programmatic options configuration works
- configuration binding from `IConfiguration` works
- unsafe configured values above internal safety ceilings are rejected
- invalid configuration is not silently clamped
- true protocol/internal invariants remain non-configurable where appropriate

For sample applications, verify configuration examples are represented in `appsettings.json` / `appsettings.Development.json` rather than hardcoded in `Program.cs`.

# Test Technology

Prefer:
- xUnit
- FluentAssertions only if already approved/available; otherwise use standard assertions
- NSubstitute/Moq only when a substitute adds real value
- ASP.NET Core TestServer/WebApplicationFactory for endpoint-level integration tests
- custom deterministic fake WebSocket/message-source objects for hot-path unit tests

Avoid excessive mocking. Prefer state-based tests and small fakes.

# Naming

Use behavior-oriented names such as:

```text
RouteAsync_WhenUserHasThreeConnections_EnqueuesForAllThree
Heartbeat_WhenPongNotReceivedWithinTimeout_RemovesConnection
Enqueue_WhenBufferIsFullAndPolicyIsDisconnect_DisconnectsClient
```

# Output Expectations

At the end of each cycle, report:
- tests added
- expected failing reason
- exact command used to run tests
- failures unrelated to the new test
- production behavior that must be implemented next

Never weaken a test merely to make production code pass.
