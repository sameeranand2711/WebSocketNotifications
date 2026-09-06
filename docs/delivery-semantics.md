# Delivery Semantics

V1 successful delivery to one connection means:

```text
that connection's WebSocket send operation completed successfully
```

This is a transport-side definition. It does not guarantee that:

- the browser received the bytes before disconnecting;
- client application code processed the payload;
- a user viewed the notification;
- the client acknowledged it;
- an offline client will receive it later;
- delivery is exactly once.

## Publishing versus sending

`WebSocketNotificationHub.PublishAsync` resolves current recipients, serializes the notification, enforces the outbound limit, and offers it to each bounded connection buffer. Completion means local routing acceptance, not completion of every subsequent socket send. The message-source handler has the same acceptance boundary.

The connection's single sender later performs the actual send. A failed send terminates and cleans up that connection. The failure is observed by the connection lifetime but is not converted into a replay or provider-independent retry.

## No WebSocket retry

V1 never retries a WebSocket notification. Retrying after an ambiguous transport failure can create duplicates and would require application acknowledgement semantics that V1 deliberately does not define.

Applications that need confirmed processing can define an application-specific inbound acknowledgement message and durable state outside this library. They must choose identifiers, timeouts, duplicate handling, and retry policy themselves.

## Upstream duplicates and loss windows

An at-least-once source may redeliver the same `MessageId`, and the core does not deduplicate it. Notifications published while a client is disconnected are not stored or replayed. Applications should make client handling idempotent where duplicate source delivery is possible.

## Slow-client outcomes

- `Disconnect`: the full connection is removed; queued messages may remain unsent.
- `DropOldest`: an older queued message can be discarded to accept the current one.
- `DropCurrent`: the current notification can be discarded for that connection.

These policies keep the shared routing path bounded and non-blocking. Choose them according to whether freshness or completeness matters more for the application.
