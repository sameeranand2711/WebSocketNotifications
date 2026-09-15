# WebSocket Notifications V1 release notes

## 1.0.0-rc.2 candidate

The RC.2 candidate targets .NET 10 only. The core package, .NET samples, and automated .NET tests use `net10.0`; development and deployment environments should install the current .NET 10 servicing update. This replaces RC.1's .NET 8 target for stable V1.

The candidate build uses locked dependency graphs and mandatory vulnerability auditing. Its NuGet output includes MIT, repository, project, and discovery metadata; SDK-provided Source Link; portable symbols in a `.snupkg`; deterministic CI metadata; and package validation. Automated inspection rejects unexpected target frameworks or package dependencies, and a clean temporary ASP.NET Core application restores only the packed package before testing authentication, endpoint mapping, DI registration, and a real WebSocket exchange.

The candidate emits a tag-free `WebSocketNotifications` meter covering active connections/subscriptions, notification routing outcomes, queue pressure, subscription command outcomes, and WebSocket send completion/failure. Kafka assignment-aware readiness remains available at `/health/ready` in the sample host. The focused security/reliability review found no Critical or High defect and removed raw authenticated user IDs from operational logs.

The recorded Stage 18 envelope covers up to four independently routed servers, 100 connections and 800 subscription associations per server, 50 shared-route notifications per second, 4 KiB payloads, slow clients, connection churn/resubscription, source interruption, server restart, and graceful shutdown. The ten-minute four-server soak completed more than 11 million sends with zero send failures and no queued, connection, or subscription state remaining after shutdown. These workstation results validate the declared V1 envelope but are not a universal throughput guarantee; see `docs/performance-soak.md`.

The RC.2 release gate passed on 2026-09-15 with a final-release quality score of **97 / 100** and every mandatory gate satisfied. The exact candidate builds with zero warnings/errors; passes 141/141 .NET tests and 6/6 client tests without skips; passes dependency audit, TypeScript, and optimized Next.js build; passes package inspection and clean external package consumption; and passes the real Kafka multi-host E2E.

The score breakdown is: correctness 30/30, automated testing 19/20, simplicity/maintainability 14/15, concurrency/reliability/resource safety 14/15, performance readiness 8/8, security/abuse resistance 5/5, and documentation/developer experience 7/7. The concrete deductions are the lack of an automated real-browser UI E2E, the maintenance surface of the dedicated internal performance harness, and the ten-minute rather than multi-hour target-hardware soak.

The V1 public API and wire protocol are frozen at this RC.2 gate. Until stable publication, only release-blocking fixes should alter them, and any such fix must repeat the affected gates. RC.2 is not the approved stable release: prerelease feedback, repository-owner personal testing, approval for the final PR to `main`, and separate explicit approval for stable publication still remain.

## Historical 1.0.0-rc.1 notes

> These notes describe the historical RC.1 artifact. After RC.1, stable V1 selected source-level multi-server fan-out. RC.1 still uses a fixed Kafka consumer group and is not scale-out ready; implementation and two-host proof are required for RC.2.

WebSocket Notifications V1 provides a provider-neutral .NET 8 library for routing application notifications to authenticated WebSocket clients. It includes opaque application-defined subscriptions, authenticated direct-user delivery, bounded slow-client handling, heartbeat and size controls, a KafkaHighThroughput host and producer Web API, and a reconnecting Next.js 16 sample client.

## Try it

With Docker Desktop running:

```powershell
dotnet test WebSocketNotifications.slnx --configuration Release
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-live-e2e.ps1
```

See the [README](README.md) for manual host, producer, and browser-client instructions.

## Compatibility

- Core and .NET samples target .NET 8.
- The repository pins .NET SDK 10.0.101 for its verified development environment.
- The browser sample uses Next.js 16.3.4, React 19.2.8, and Node.js 20.9 or newer.
- The local broker environment uses the official Apache Kafka 4.2.1 container.

## Important limitations

V1 assumes one WebSocket server with in-memory state. It has no distributed backplane, replay, offline store, built-in acknowledgement tracking, WebSocket retry, deduplication, exactly-once guarantee, tenant model, binary protocol, subscription TTL, or multi-region support.

Successful delivery means a WebSocket send completed; it does not mean the client application processed or acknowledged the notification. Notifications can be missed while disconnected and upstream at-least-once delivery can produce duplicates.

## Release quality

Final score: **94 / 100 — Strong V1**

All mandatory release gates pass:

- Release solution build succeeds with zero warnings and errors.
- 106/106 .NET tests and 6/6 client tests pass with no skips.
- TypeScript checking and the optimized Next.js build pass.
- The real producer → Kafka → hosted adapter → WebSocket → reusable Next.js client flow passes.
- Core dependency, authentication, authorization, bounded-memory, ordering, expiry, size-limit, cancellation, configuration, and documentation gates pass.

The principal RC.1 residual risks are its single-instance sample configuration, lack of a published sustained-load benchmark, and the absence of application acknowledgement/replay semantics.
