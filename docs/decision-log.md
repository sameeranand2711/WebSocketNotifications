# Decision Log

## Provider-neutral source boundary

The core accepts `NotificationEnvelope` through `INotificationMessageSource`. Provider deserialization, connection, commit/ACK, and retry behavior remains outside the package. This keeps the core usable with Kafka, RabbitMQ, ZeroMQ, or direct application publishing without becoming a broker framework.

## Application-owned identity

ASP.NET Core performs authentication and `IWebSocketUserResolver` maps the authenticated request to an application user ID. The library contains no JWT-specific code and the client cannot subscribe to another direct user.

## Application-controlled subscriptions

Client subscriptions are limited to group, feed, and event type. `ISubscriptionAuthorizer` is called before mutation and defaults to deny. Authorization rules remain in the consuming application's domain.

## Single server for V1

Connections and subscriptions are indexed in memory for a simple, predictable first release. No Redis backplane, server IDs, distributed presence, or partial multi-server protocol was added. A future distributed resolver can precede local routing without changing the neutral envelope.

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
