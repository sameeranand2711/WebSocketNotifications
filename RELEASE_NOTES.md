# WebSocket Notifications V1 release notes

## RC.2 candidate compatibility

The developing RC.2 candidate targets .NET 10 only. The core package, .NET samples, and automated .NET tests use `net10.0`; development and deployment environments should install the current .NET 10 servicing update. This replaces RC.1's .NET 8 target for stable V1.

The RC.2 candidate is not yet the approved stable release. These compatibility notes record the selected runtime matrix; final test evidence and release authorization are added only after the remaining release stages pass and the repository owner completes personal testing.

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
