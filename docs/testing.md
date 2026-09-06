# Testing

## Automated suites

The xUnit project mirrors the production responsibilities:

- `Contracts`: envelope validation, defensive copies, UTC timestamps, and expiry boundaries
- `Configuration`: defaults, configuration binding, valid ranges, and deferred validation
- `Connections`: lifecycle, multiple connections per user, subscription cleanup, bounded policies, sender/receiver ownership, failure, fragmentation, size limits, concurrency, and heartbeat
- `Delivery`: route union/deduplication, expiry, serialization, buffer isolation, ordering, and programmatic hub behavior
- `Protocol`: authorization, subscribe/unsubscribe, heartbeat pong, malformed input, and application messages
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

ASP.NET Core TestServer tests use real WebSocket connections to verify authenticated connection setup, direct delivery to multiple connections, FIFO delivery, and authorized subscription routing. Sample integration tests exercise real KafkaHighThroughput adapter types without needing a running broker.

## Live end-to-end validation

Docker Desktop must be running. The runner uses a unique user for each execution, starts the sample host and actual reusable JavaScript client, publishes through Kafka, and checks the returned notification marker:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-live-e2e.ps1
```

Expected final line:

```text
LIVE E2E PASSED: producer -> Kafka -> host -> WebSocket -> Next.js client (...)
```

Process logs are written to the ignored `.e2e` directory when diagnosis is needed. The Kafka container remains running for subsequent sample use; stop it with `docker compose down` when finished.

## Current release evidence

- 105 .NET tests passing with no skips
- 6 Node tests passing
- Strict TypeScript check passing
- Next.js optimized production build passing
- Live Kafka end-to-end flow passing
- Release .NET build with zero warnings and errors

No coverage percentage or dedicated load benchmark is published for V1. Tests verify bounded memory behavior and non-blocking buffer overflow directly; sustained load benchmarking is a documented post-V1 improvement rather than an implied result.
