# Testing

All .NET projects target `net10.0`. The supported and required test runtime for V1 is .NET 10 with the current servicing update.

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
dotnet restore WebSocketNotifications.slnx --locked-mode
dotnet build WebSocketNotifications.slnx --configuration Release --no-restore
dotnet test WebSocketNotifications.slnx --configuration Release --no-build --no-restore
```

Frontend validation:

```powershell
cd samples/clients/websocket-notifications-nextjs
npm ci
npm audit --audit-level=high
npm test
npm run typecheck
npm run build
```

## Package validation

The release package enables NuGet package validation, portable symbols, Source Link, and deterministic CI build metadata. Build it, inspect its exact contents, and consume it from a clean temporary ASP.NET Core application:

```powershell
dotnet pack src/WebSocketNotifications/WebSocketNotifications.csproj --configuration Release --no-build --no-restore --output artifacts/packages/current
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/inspect-package.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-package-smoke-test.ps1
```

The inspection requires one `.nupkg` and one `.snupkg`, the `net10.0` library and XML documentation, MIT/project/repository metadata, the GitHub Source Link mapping, and no package dependencies. The smoke runner copies a standalone project outside the repository, restores `WebSocketNotifications` only from the local package directory, and proves custom authentication, user resolution, DI registration, endpoint mapping, and direct-user WebSocket delivery without a project reference.

## Continuous integration

`Build, test, and package` performs a locked audited restore, warning-free Release build, all .NET tests with skipped tests configured as failures, package validation/inspection/smoke testing, dependency audit, JavaScript tests, TypeScript checking, and the optimized Next.js build. It retains both NuGet artifacts.

`Multi-host Kafka E2E` runs the real broker scenario in a separate required job and retains `.e2e` diagnostics on failure. Both workflows run for V1 stage/cumulative pushes and pull requests targeting `main`; caches contain dependencies only, never generated build output.

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

- 135 .NET tests passing on .NET 10 with no skips
- 6 Node tests passing
- Strict TypeScript check passing
- Next.js optimized production build passing
- Real Kafka shared-group negative control and independent two-host fan-out passing
- Real Kafka host-continuity and no-replay restart scenarios passing
- Release .NET 10 build with zero warnings and errors

No coverage percentage or dedicated load benchmark is published for V1. Tests verify bounded memory behavior and non-blocking buffer overflow directly; sustained load benchmarking is a documented post-V1 improvement rather than an implied result.
