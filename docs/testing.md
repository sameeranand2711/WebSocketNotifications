# Testing

## Automated suites

The xUnit project mirrors the production responsibilities:

- `Contracts`: envelope validation, defensive copies, UTC timestamps, and expiry boundaries
- `Configuration`: defaults, configuration binding, valid ranges, and deferred validation
- `Connections`: lifecycle, multiple connections per user, subscription cleanup, bounded policies, sender/receiver ownership, failure, fragmentation, size limits, concurrency, and heartbeat
- `Delivery`: route union/deduplication, expiry, serialization, buffer isolation, ordering, and programmatic hub behavior
- `Protocol`: opaque-key authorization, batch subscribe/unsubscribe, duplicate handling, atomic denial, heartbeat pong, malformed input, and application messages
- `Hosting`: DI registration, optional source behavior, authentication/identity boundaries, and real TestServer WebSockets
- `Samples`: Kafka adapter handoff/failure propagation and producer API routing, validation, key, and receipt behavior

The Next.js tests cover protocol parsing, reconnect-loop deduplication, resubscription, unsubscribe behavior, intentional disconnect, heartbeat pong, and notification dispatch.

## Commands

From the repository root:

```powershell
dotnet test WebSocketNotifications.slnx --configuration Release
```

Frontend validation:

```powershell
cd samples/clients/websocket-notifications-nextjs
npm install
npm test
npm run typecheck
npm run build
```

## Integration coverage

ASP.NET Core TestServer tests use real WebSocket connections to verify authenticated connection setup, direct delivery to multiple connections, FIFO delivery, authorized subscription routing, and local routing behavior across two host instances. Sample integration tests exercise real KafkaHighThroughput adapter types and the generated per-process group configuration without needing a running broker.

## Live end-to-end validation

Docker Desktop must be running. The multi-host runner creates a unique Kafka topic and proves:

- two hosts in one shared group receive only one copy between them (negative control)
- two hosts in independent namespaced groups each receive one copy
- one host continues after its peer stops
- a restarted host with a new group does not replay a notification emitted while offline

It waits for Kafka partition-assignment readiness before connecting clients or publishing:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-multi-host-e2e.ps1
```

Expected final line:

```text
MULTI-HOST E2E PASSED: shared-group negative control, independent fan-out, host continuity, and no-replay restart (...)
```

On success the runner removes its processes, topic, Kafka container, and per-run log directory. On failure it keeps the ignored per-run logs under `.e2e` for diagnosis. Pass `-LeaveKafkaRunning` to retain the Kafka container for subsequent sample use.

## Current release evidence

- 118 .NET tests passing with no skips
- 6 Node tests passing
- Strict TypeScript check passing
- Next.js optimized production build passing
- Real Kafka shared-group negative control and independent two-host fan-out passing
- Real Kafka host-continuity and no-replay restart scenarios passing
- Release .NET build with zero warnings and errors

No coverage percentage or dedicated load benchmark is published for V1. Tests verify bounded memory behavior and non-blocking buffer overflow directly; sustained load benchmarking is a documented post-V1 improvement rather than an implied result.
