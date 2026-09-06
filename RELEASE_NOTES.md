# WebSocket Notifications 1.0.0-preview.1

WebSocket Notifications V1 provides a provider-neutral .NET 8 library for routing application notifications to authenticated WebSocket clients. It includes group/feed/event subscriptions, direct-user delivery, bounded slow-client handling, heartbeat and size controls, a KafkaHighThroughput host and producer Web API, and a reconnecting Next.js 16 sample client.

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
- 105/105 .NET tests and 6/6 client tests pass with no skips.
- TypeScript checking and the optimized Next.js build pass.
- The real producer → Kafka → hosted adapter → WebSocket → reusable Next.js client flow passes.
- Core dependency, authentication, authorization, bounded-memory, ordering, expiry, size-limit, cancellation, configuration, and documentation gates pass.

The principal residual risks are the intentionally single-instance design, lack of a published sustained-load benchmark, and the absence of application acknowledgement/replay semantics.
