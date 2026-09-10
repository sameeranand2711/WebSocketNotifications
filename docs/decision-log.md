# Decision Log

## Provider-neutral source boundary

The core accepts `NotificationEnvelope` through `INotificationMessageSource`. Provider deserialization, connection, commit/ACK, and retry behavior remains outside the package. This keeps the core usable with Kafka, RabbitMQ, ZeroMQ, or direct application publishing without becoming a broker framework.

## Application-owned identity

ASP.NET Core performs authentication and `IWebSocketUserResolver` maps the authenticated request to an application user ID. The library contains no JWT-specific code and the client cannot subscribe to another direct user.

## Application-controlled subscriptions

Subscriptions are opaque string keys rather than library-defined group, feed, or event categories. `ISubscriptionAuthorizer` receives each key before mutation and defaults to deny. Naming, tenant/partner scoping, and authorization rules remain in the consuming application's domain.

Direct `UserIds` remain separate because direct routing is derived from authenticated identity and cannot be changed through client subscription commands. The pre-V1 category-specific `NotificationSubscription` and `SubscriptionKind` types were removed rather than deprecated.

## Single server for V1 (superseded)

RC.1 originally limited V1 to one server with in-memory connection and subscription indexes. The indexes remain local, but the one-server release decision was superseded after identifying that a shared queue consumer group could send a notification to a server with no matching local connection.

## Source-level fan-out for stable V1

Every active WebSocket server independently receives each cluster-wide notification and performs local routing. Kafka-backed servers use independent consumer groups; other providers supply equivalent independent subscriptions. This supports connections for the same user or subscription across several servers without distributed presence.

Kafka groups are namespaced by application and environment and contain an instance ID unique to the active process incarnation. Restarting creates a new group that begins at the live end, preserving V1's explicit no-replay behavior. Broker retention cleans obsolete ephemeral group metadata.

Server identity remains adapter/deployment metadata and is not added to `NotificationEnvelope`. `WebSocketNotificationHub.PublishAsync` remains local, while cluster-wide delivery uses the shared source. V1 remains live and non-durable, so restarted servers do not replay notifications emitted while offline.

Distributed presence and targeted per-server delivery remain possible future optimizations if measured fan-out cost requires them; Redis is not introduced speculatively.

## Bounded per-connection buffers

Every connection has a configurable bounded channel. Overflow is explicit (`Disconnect`, `DropOldest`, or `DropCurrent`) and routing never awaits capacity. This prevents a slow client from indefinitely blocking shared source processing.

## One sender per connection

Only the connection sender calls `WebSocket.SendAsync`. This satisfies WebSocket concurrency requirements and preserves accepted per-connection order without a global network lock.

## No WebSocket retries or built-in ACKs

Ambiguous transport failures cannot be retried safely without application semantics. Send failure ends the connection. Client processing acknowledgement, retry, replay, and durable state belong to the application.

## JSON text protocol

V1 uses a compact JSON text protocol because notification payloads are already represented as `JsonElement` and browsers support it directly. Binary framing and negotiation are deferred.

## Application-owned Kafka key

Kafka keys express a business ordering domain such as a user, feed, or order. The producer selects the key; the WebSocket core neither creates nor carries it and promises no global ordering across partitions.

## Configuration validation without unrelated eager failure

Unsafe values are rejected and never clamped. Registration alone does not eagerly resolve the optional subsystem, allowing a broader host that does not use it to start; mapping or using the subsystem surfaces all validation failures.

## Responsibility-based namespaces

Public contracts, abstractions, configuration, delivery, and hosting APIs use namespaces matching their directories. Internal connection and protocol components remain in their corresponding namespaces. This was finalized before V1 release to avoid a later namespace-breaking change.
