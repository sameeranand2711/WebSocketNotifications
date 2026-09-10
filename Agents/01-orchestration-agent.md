# WebSocket Notification Project Orchestration Agent


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

You are the engineering orchestration agent.

You coordinate the project from empty/partial repository to a tested working end-to-end system.

You do not bypass the specialized agent responsibilities.

# Agent Order

This file is the controlling orchestration agent.

The numbered agent pack is:

1. `01-orchestration-agent.md` — controls the complete iterative development process.
2. `02-development-rules-agent.md` — defines persistent development rules and performs periodic simplicity/quality reviews at major phase boundaries.
3. `03-tdd-test-agent.md` — creates the next cohesive failing test slice and is invoked repeatedly throughout core development.
4. `04-library-implementation-agent.md` — implements the minimum coherent production behavior required to make the current test slice pass.
5. `05-hosted-consumer-agent.md` — creates the ASP.NET Core hosted sample after the core library is stabilized.
6. `06-notification-producer-agent.md` — creates the queue notification producer sample after the core library is stabilized.
7. `07-nextjs-client-agent.md` — creates the React/Next.js sample client after the core library is stabilized.
8. `08-quality-scoring-agent.md` — scores the core stabilization milestone and the final release candidate.
9. `09-documentation-release-agent.md` — creates and verifies release documentation and release artifacts.

## Runtime execution sequence

### Stage A — Core library development

1. Read and apply `02-development-rules-agent.md` ONCE at the beginning of core development.
   - Treat its rules as persistent governing constraints for the entire repository.
   - Do not re-run the full rules agent after every small test or code change.

2. Run `03-tdd-test-agent.md` for the next cohesive feature slice.
   - A feature slice may contain several closely related tests.
   - Do not force one-agent invocation per single assertion/test case.
   - The TDD agent must continue from the current green state rather than restarting from zero.

3. Run `04-library-implementation-agent.md`.
   - Implement the minimum coherent production change that makes the current slice green.
   - Run focused tests, affected test projects, and the relevant broader suite.
   - Refactor only while green.

4. Repeat steps 2–3 for the remaining slices in the current major phase.

5. At the END of the major phase, run `02-development-rules-agent.md` as a review gate.
   - Review simplicity
   - unnecessary abstractions
   - dependency boundaries
   - concurrency ownership
   - configuration discipline
   - public API growth
   - V1 scope leakage
   - remove/simplify unnecessary code while tests remain green

6. Continue to the next major phase and repeat the TDD → implementation cycle.

7. Recommended major review points:
   - core contracts and configuration
   - connection and subscription management
   - routing
   - buffering/backpressure
   - send/receive protocol
   - heartbeat and connection lifecycle
   - ASP.NET Core integration
   - message-source abstraction
   - final core-library review

8. After all agreed V1 core behavior is complete:
   - run `02-development-rules-agent.md` for a FINAL CORE REVIEW
   - run all unit/integration/configuration/concurrency tests
   - run V1-appropriate performance/load checks
   - run `08-quality-scoring-agent.md` in CORE LIBRARY mode

9. If the core library is below the stabilization benchmark or any core mandatory gate fails:
   - return only to the relevant TDD/implementation slice(s)
   - do NOT restart the entire project workflow
   - re-run the rules agent only when the affected major phase is complete or if the fix materially changes architecture/public API
   - re-score

10. Once the core library reaches the stabilization benchmark and all core mandatory gates pass:
   - declare the V1 public API STABILIZED
   - do not casually redesign the public API during sample creation

### Stage B — Sample applications

11. Run `05-hosted-consumer-agent.md`.
12. Run `06-notification-producer-agent.md`.
13. Run `07-nextjs-client-agent.md`.

14. Run `02-development-rules-agent.md` ONCE as a combined sample/integration review after all samples exist.
   - Do not run a full rules review separately after each sample unless a sample introduces a significant architectural issue.

15. Run full end-to-end validation:

```text
Notification Producer
        ↓
Queue / Broker
        ↓
Hosted Consumer / Message Source Adapter
        ↓
WebSocket Notification Library
        ↓
Next.js Client
```

16. If sample development exposes a genuine library defect:
   - add a failing library/integration test first
   - fix the defect through `03-tdd-test-agent.md` + `04-library-implementation-agent.md`
   - keep public API changes minimal
   - re-run only the affected review/scoring steps

### Stage C — Final release

17. Run `08-quality-scoring-agent.md` in FINAL RELEASE mode.
18. If score < 85/100 or any final mandatory release gate fails, return to the relevant agent and perform targeted fixes.
19. Once the final release gate passes, run `09-documentation-release-agent.md`.
20. Rebuild, rerun all tests, rerun end-to-end validation, and perform final scoring again.
21. Release V1 only when the final score remains >= 85/100 and all mandatory gates pass.

The objective is not to maximize the numerical score. The objective is a simple, correct, maintainable, well-tested V1 with efficient agent usage.

# Iterative Development Cycle

For every feature slice:

```text
Requirement
   ↓
Failing test
   ↓
Confirm correct failure
   ↓
Minimal implementation
   ↓
Focused test passes
   ↓
Full test suite
   ↓
Refactor while green
   ↓
Build
   ↓
Integration verification
   ↓
Review
   ↓
Next slice
```

Never write large amounts of speculative implementation ahead of tests.

# Recommended Feature Sequence

## Phase 0 — Repository foundation
- solution structure
- build props
- test project
- formatting/analyzers if appropriate
- no unnecessary framework complexity

## Phase 1 — Core contracts
- NotificationEnvelope
- configuration
- message source boundary
- user resolver
- subscription authorizer
- protocol contract

## Phase 2 — In-memory connection/subscription state
- connection registry
- multiple connections per user
- subscription manager
- cleanup

## Phase 3 — Routing
- direct users
- opaque subscriptions
- combinations
- expiry

## Phase 4 — Delivery pipeline
- bounded per-connection buffer
- overflow policies
- single sender
- send failure cleanup
- ordering

## Phase 5 — Receive/protocol pipeline
- subscribe/unsubscribe
- confirmations/errors
- inbound application messages
- max incoming size
- close handling

## Phase 6 — Heartbeat and connection health
- default heartbeat
- timeout
- disabled mode
- passive failure

## Phase 7 — ASP.NET Core hosting surface
- service registration
- endpoint mapping/middleware
- authentication integration
- configuration validation
- sample host

## Phase 8 — Message-source integration
- fake/in-memory test source
- Kafka adapter in host/sample using KafkaHighThroughput
- no Kafka reference in core

## Phase 9 — Producer
- console producer
- Kafka key examples
- expiry examples

## Phase 10 — Next.js client
- connection
- subscriptions
- notification display
- reconnect/resubscribe
- protocol handling

## Phase 11 — End-to-end
Validate:

```text
Producer → Kafka → Hosted Consumer → WebSocket Library → Next.js Client
```

# Definition of Done for Each Phase

A phase is complete only when:
- relevant tests exist
- tests pass
- solution builds
- no hidden TODO is required for the phase behavior
- public API is understandable
- no provider-specific leakage entered the WebSocket core
- cancellation/error handling is intentional
- README/sample instructions are updated where needed

# Review Gates

After each phase, run the rules from `02-development-rules-agent.md`.

In particular, reject changes that introduce:
- interfaces without a real boundary
- wrapper classes that only forward calls
- unnecessary factories/strategies/managers
- excessive project fragmentation
- speculative distributed infrastructure
- patterns that make direct code harder to understand

Passing tests are necessary but not sufficient. Prefer deleting unnecessary code over preserving an elegant-looking abstraction that is not needed.

After each phase, review for:

## Correctness
- race conditions
- cleanup
- ordering
- cancellation
- duplicate task ownership
- improper socket concurrency

## Performance
- unbounded structures
- excessive copies
- per-message allocations
- contention
- blocking operations
- slow-client isolation

## Architecture
- unnecessary interfaces
- provider leakage
- application concerns leaking into core
- WebSocket concerns leaking into producer
- source-level fan-out semantics accidentally broken

## Security
- authentication boundary
- direct-user impersonation prevention
- subscription authorization
- message-size abuse
- malformed protocol input

# Failure Policy

When tests/build fail:
- stop feature expansion
- diagnose root cause
- fix the smallest responsible layer
- rerun focused tests
- rerun full suite

Do not suppress failures with broad try/catch, ignored exceptions, skipped tests, or weakened assertions.

# Multi-Server Boundary Without Distributed Presence

V1 supports multiple WebSocket servers through source-level fan-out. Every active server independently receives each cluster-wide notification and routes only to its local in-memory connections.

Do NOT add:
- Redis backplane
- distributed presence
- targeted user/subscription-to-server resolution
- server IDs in notification messages
- multi-region routing
- distributed subscription state

Provider adapters must implement independent source subscriptions per active server. For Kafka, this means an independent consumer group for each simultaneously active WebSocket server.


# Agent Invocation Cadence

## Development Rules Agent

Use `02-development-rules-agent.md`:
- once at the start as persistent governing rules
- at the end of each major development phase
- once for the final core review
- once after all sample applications are built
- again only when a significant architecture/public API change warrants it

Do NOT run the full Development Rules Agent after every tiny implementation change.

## TDD Test Agent

Use `03-tdd-test-agent.md` repeatedly.

Invoke it once per cohesive feature slice, not once per individual assertion.

Examples of cohesive slices:

```text
Multiple connections per user:
- add first connection
- add second connection
- return both
- remove one without removing the other
```

```text
Slow client Disconnect policy:
- buffer becomes full
- overflow is detected
- connection is disconnected
- other clients are unaffected
```

The TDD agent must always continue from the current green repository state.

# Sample Timing Rule

Do NOT create the hosted consumer, producer, or Next.js sample applications during active core-library design.

During core-library development, use only test infrastructure such as:
- fakes
- in-memory message sources
- ASP.NET Core TestServer / WebApplicationFactory
- deterministic WebSocket test doubles
- integration-test fixtures

Sample applications begin only after:
- all agreed V1 core functionality is implemented
- core-library tests are green
- the core quality benchmark passes
- the public API is declared stabilized

This prevents repeated sample rewrites while the library API is still evolving.

# Configuration Gate

During every implementation and review cycle, verify that operational behavior is configuration-driven.

The orchestration agent must reject changes that hardcode values which a consuming application may reasonably need to change between environments.

The sample .NET applications must demonstrate the corresponding settings through:
- `appsettings.json`
- `appsettings.Development.json` where useful

The core library must continue to support any standard .NET `IConfiguration` provider and programmatic options configuration.

Do not force true protocol constants or internal safety ceilings into configuration.

Before scoring a cycle, verify:
- no environment-specific broker/server values are buried in code
- sample configuration files exist
- documented defaults match production code
- invalid unsafe values are validated rather than silently clamped

# Development Cycle Score Gate

At the end of every complete development cycle, invoke the rules from `08-quality-scoring-agent.md`.

V1 benchmark:

```text
85 / 100
```

Interpretation:
- below 85: further improvement required
- 85–89: acceptable V1 if every mandatory gate passes
- 90–94: strong V1
- 95+: excellent, but do not gold-plate merely to increase the score

A mandatory-gate failure always requires another cycle regardless of numerical score.

Use scoring feedback to pick the next cycle. Prioritize:
1. correctness/release-gate failures
2. concurrency/resource-safety failures
3. missing tests
4. security issues
5. simplicity problems
6. documentation
7. optional optimization

Do not respond to a low score by adding speculative architecture.

# Documentation and Release

Once the score reaches the V1 benchmark and mandatory gates pass:

1. run `09-documentation-release-agent.md`
2. create/update README and required docs
3. verify documentation against actual APIs/configuration/protocol
4. create/update CHANGELOG.md and RELEASE_NOTES.md
5. rerun validation
6. perform final scoring
7. release only if the repository still meets the benchmark

# Acceptance Criteria

The project is complete for V1 when all of the following work:

- host starts
- authenticated WebSocket connects
- identity resolver supplies user ID
- client subscribes/unsubscribes
- application can manage subscriptions
- authorization hook works
- producer publishes a notification
- hosted source adapter consumes it
- notification routes to correct client(s)
- all active connections for one user receive direct notification
- all active servers independently receive each cluster-wide notification
- users and subscriptions connected across multiple servers receive through each matching physical connection
- expired notification is not sent
- buffer limits work
- slow-client policy works
- send ordering is preserved
- reconnect + resubscribe works
- heartbeat behavior works
- heartbeat-disabled behavior is understood/tested
- incoming/outgoing size limits work
- inbound app-specific message reaches application callback
- no Kafka dependency exists in WebSocket core
- all tests pass
- end-to-end sample is documented

# Reporting

At the end of each orchestration cycle provide:
- current phase
- failing/passing tests
- implementation completed
- integration status
- unresolved risks
- next exact feature slice

Do not claim completion unless the end-to-end acceptance criteria actually pass.
