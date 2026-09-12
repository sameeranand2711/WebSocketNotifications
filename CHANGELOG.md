# Changelog

All notable changes to this project are documented here.

## [Unreleased]

### Changed

- Selected source-level fan-out as the stable V1 multi-server architecture: every active WebSocket server independently receives cluster-wide notifications and performs local in-memory routing.
- Superseded the RC.1 single-server release assumption without adding distributed presence or server identity to the notification envelope.
- Added per-process Kafka consumer groups named `{application}.{environment}.{instance-id}` with an explicit instance override and an ephemeral startup identity fallback.
- Changed the hosted Kafka sample to start unseen groups at the live end and expose assignment-aware source readiness at `/health/ready`.
- Added deterministic real-Kafka proof of the shared-group failure mode, independent two-host fan-out, continued delivery after one host stops, and no replay after restart.
- Bounded per-connection subscription count and subscription-key length with validated defaults and hard ceilings.
- Added atomic subscription-batch rejection with `subscription_limit_exceeded` and `subscription_key_too_long` protocol errors.
- Selected .NET 10 LTS as the only V1 target and retargeted the library, tests, and .NET samples to `net10.0`.
- Added the sequential cumulative V1 release-agent workflow and release checklist.

## [1.0.0-rc.1] - 2026-09-09

### Added

- Provider-neutral .NET 8 WebSocket notification library
- Authenticated ASP.NET Core endpoint with application user resolution
- Direct-user routing and opaque application-defined subscription routing
- Application-controlled subscription authorization and programmatic management
- Bounded connection buffers with three slow-client policies
- Single send/receive loops, heartbeat, compression option, and size limits
- Neutral message-source abstraction and direct publishing hub
- KafkaHighThroughput hosted consumer and multi-endpoint producer Web API samples
- Swagger UI and an OpenAPI document for all producer notification endpoints
- Reconnecting/resubscribing Next.js 16 client and sample UI
- Next.js clients organized under `samples/clients`
- Unit, integration, protocol, concurrency, sample, and live Kafka E2E validation
- Explicit `users` and `subscriptions` request fields on the corresponding producer endpoints

### Behavior

- Direct notifications reach all current connections for a user.
- Multiple route matches are deduplicated per connection.
- Notifications expire at the inclusive UTC `ExpiresAt` boundary.
- Per-connection accepted ordering is preserved by one FIFO sender.
- Source callback completion represents bounded local routing acceptance; successful connection delivery means WebSocket send completion.
- Local Kafka readiness waits for the consumer-group coordinator before starting the samples.

### Known limitations

- Single WebSocket server with in-memory state
- No replay, durable offline store, distributed backplane, built-in acknowledgement, WebSocket retry, deduplication, or exactly-once guarantee
- JSON text only; tenant scopes, binary protocol, subscription TTL, and multi-region routing are deferred
