# Architecture

## Processing flow

```mermaid
flowchart LR
    A[Application message source] --> B[Notification processing]
    B --> C[Recipient routing]
    C --> D[Bounded connection buffer]
    D --> E[Single send loop]
    E --> F[WebSocket client]
```

The consuming application deserializes provider data into a `NotificationEnvelope`. The core checks expiry, resolves the union of direct-user and subscription matches, serializes one JSON frame, and attempts a non-blocking write to each recipient's bounded buffer. One loop per connection drains that buffer into its WebSocket.

## Responsibilities

- `WebSocketNotifications.Contracts` defines the neutral notification envelope.
- `WebSocketNotifications.Abstractions` defines application/provider boundaries for message sources, identity, authorization, and inbound application messages.
- `WebSocketNotifications.Configuration` owns public settings and validation.
- `WebSocketNotifications.Connections` owns in-memory connection indexes, buffers, socket loops, heartbeats, and session cleanup.
- `WebSocketNotifications.Delivery` owns recipient resolution, serialization, publishing, and programmatic subscription operations.
- `WebSocketNotifications.Protocol` parses the V1 client control protocol.
- `WebSocketNotifications.Hosting` supplies ASP.NET Core registration, endpoint mapping, and optional source-worker integration.

Provider-specific queue logic stays outside the core. Kafka commit/retry, RabbitMQ ACK/NACK, deserialization, credentials, and connection management cannot leak into the notification library through `INotificationMessageSource`.

## Connection lifecycle

1. ASP.NET Core authenticates the endpoint request.
2. `IWebSocketUserResolver` returns the application's stable user ID.
3. The endpoint accepts the WebSocket and creates a connection ID.
4. The connection is indexed by ID and authenticated user.
5. One sender, one receiver, and—when enabled—one heartbeat loop run for the connection.
6. Completion or failure of any owned loop cancels the others.
7. Cleanup removes the connection and all its subscriptions atomically from the in-memory indexes.

Multiple connections can share one user ID. Direct-user notifications are routed to all connections in the snapshot.

## Subscription lifecycle

Opaque string subscription keys are attached to a connection. The library neither parses prefixes nor assigns category semantics. A client subscribe request is authorized before mutation. Application code can also manage a particular connection or every current connection for a user through `WebSocketNotificationHub`. Subscriptions end on explicit removal or connection cleanup; there is no TTL in V1.

Applications own key conventions and authorization, including tenant or partner namespacing such as `tenant:abc:group:premium`. Direct-user routing is not a subscription: `UserIds` are matched only against identity derived from the authenticated request, preventing a client from claiming another direct recipient through the protocol.

## Outbound flow

Direct-user and generic-subscription matches are unioned and deduplicated. Expired notifications produce no recipients. The notification is serialized once, checked against the configured outbound limit, and offered to each buffer without awaiting network I/O. Buffer overflow follows the configured slow-client policy. Each connection's single sender preserves its buffer order and is the only code that calls `WebSocket.SendAsync` for that socket.

## Inbound flow

The single receive loop assembles fragmented text messages within the inbound limit. Library messages handle subscribe, unsubscribe, and heartbeat pong operations. Other valid JSON objects are cloned and passed to the optional `IWebSocketInboundMessageHandler`. Binary and oversized messages close the connection with the corresponding WebSocket status.

## Concurrency and ownership

Connection and subscription indexes share one short critical section; no network operation or application callback occurs while it is held. Recipient queries return snapshots. Per-connection channels are bounded and have one reader. Session lifetime owns cancellation, task observation, channel completion, and registry cleanup.

## V1 deployment boundary

V1 supports one WebSocket server instance. Connections and subscriptions are process-local, so another instance cannot resolve these recipients. A future distributed recipient resolver or backplane can be introduced between source processing and local routing, but V1 does not define or implement it and the neutral envelope contains no server identity.
