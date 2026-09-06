# WebSocket Protocol

V1 is a JSON text protocol. Property names and command values are case-sensitive.

## Connect

Connect with an HTTP WebSocket upgrade to the configured endpoint (default `/ws/notifications`). The endpoint requires ASP.NET Core authorization. Authentication credentials are application-defined, normally an existing secure cookie or another browser-compatible mechanism. The local sample alone uses `?userId=...` as a demonstration authentication scheme.

The application-provided `IWebSocketUserResolver` must return a nonblank user ID. Failure to resolve identity returns HTTP 401 before upgrade. A normal HTTP request without a WebSocket upgrade returns HTTP 400.

Clients cannot subscribe to user IDs. Direct-user delivery always uses the resolved authenticated identity.

## Subscribe

```json
{"type":"subscribe","requestId":"request-1","kind":"group","value":"operators"}
```

`kind` must be exactly `group`, `feed`, or `eventType`; `value` must be nonblank. `requestId` is optional but recommended for correlating responses. The application authorizer runs before the subscription is added.

Success:

```json
{"type":"subscribed","requestId":"request-1"}
```

Denied:

```json
{
  "type":"error",
  "requestId":"request-1",
  "code":"subscription_denied",
  "message":"The application denied this subscription."
}
```

Invalid subscription fields use code `invalid_subscription`.

## Unsubscribe

```json
{"type":"unsubscribe","requestId":"request-2","kind":"feed","value":"match-42"}
```

The operation is idempotent for the current connection and returns:

```json
{"type":"unsubscribed","requestId":"request-2"}
```

## Notification

```json
{
  "type":"notification",
  "messageId":"score-42",
  "payload":{"score":7},
  "createdAt":"2026-09-06T09:30:00Z",
  "expiresAt":"2026-09-06T09:31:00Z"
}
```

`expiresAt` is `null` when no expiry was supplied. Routing targets are not exposed to clients. A connection matching multiple routing dimensions receives one notification frame.

## Heartbeat

When heartbeat is enabled, the server sends:

```json
{"type":"ping","nonce":"e507e4d4fead4907945720f70f7c20dd"}
```

The client must respond with the same nonce before `HeartbeatTimeout`:

```json
{"type":"pong","nonce":"e507e4d4fead4907945720f70f7c20dd"}
```

A missing matching pong disconnects the connection. A pong without a string nonce receives `invalid_message`. A nonmatching nonce does not acknowledge the active probe.

## Application-specific inbound messages

Any valid JSON object with a string `type` other than `subscribe`, `unsubscribe`, or `pong` is passed to `IWebSocketInboundMessageHandler` when one is registered. Without a handler it is ignored. Applications should use their own namespaced types and must not rely on library-reserved types such as `notification`, `ping`, or `error` for client commands.

## Errors

Malformed JSON:

```json
{"type":"error","code":"invalid_message","message":"The message is not valid JSON."}
```

A non-object message or one without a string `type` also receives `invalid_message`. Null response fields are omitted. Error frames use the same bounded outgoing buffer and therefore follow its overflow policy.

## Fragmentation, limits, and close behavior

- Fragmented text frames are assembled before processing.
- A text message larger than `MaxIncomingMessageSize` closes with status 1009 (`MessageTooBig`).
- A binary message closes with status 1003 (`InvalidMessageType`).
- On a peer close frame, the server returns a close output using the peer's status when present.
- Transport send/receive failure ends the connection and removes its subscriptions.
- A serialized notification larger than `MaxOutgoingMessageSize` is rejected before it is queued.

Reconnect and resubscribe belong to the client. There is no missed-message replay.
