# WebSocket Protocol

V1 is a JSON text protocol. Property names and command values are case-sensitive.

## Connect

Connect with an HTTP WebSocket upgrade to the configured endpoint (default `/ws/notifications`). The endpoint requires ASP.NET Core authorization. Authentication credentials are application-defined, normally an existing secure cookie or another browser-compatible mechanism. The local sample alone uses `?userId=...` as a demonstration authentication scheme.

The application-provided `IWebSocketUserResolver` must return a nonblank user ID. Failure to resolve identity returns HTTP 401 before upgrade. A normal HTTP request without a WebSocket upgrade returns HTTP 400.

Clients cannot modify direct `UserIds`. Direct-user delivery always uses the resolved authenticated identity; subscription strings are a separate application-authorized routing space.

## Subscribe

```json
{"type":"subscribe","requestId":"request-1","subscriptions":["group:operators","feed:football"]}
```

`subscriptions` must be a non-empty array of nonblank strings no longer than `MaxSubscriptionKeyLength`. Duplicate keys in one command are processed once and existing duplicates do not consume quota. `requestId` is optional but recommended for correlating responses. The application authorizer receives each opaque key, and all keys are authorized before the complete batch is added atomically.

The library does not interpret prefixes. Values such as `group:operators`, `role:admin`, `tenant:abc:group:premium`, and `partner:p1:event:deposit.completed` are conventions owned entirely by the consuming application.

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

An overlong key is rejected before authorization or mutation:

```json
{"type":"error","requestId":"request-1","code":"subscription_key_too_long","message":"A subscription exceeds MaxSubscriptionKeyLength (256)."}
```

If the unique new keys would exceed `MaxSubscriptionsPerConnection`, authorization completes but the entire command is rejected without partial mutation:

```json
{"type":"error","requestId":"request-1","code":"subscription_limit_exceeded","message":"The connection subscription limit would be exceeded."}
```

## Unsubscribe

```json
{"type":"unsubscribe","requestId":"request-2","subscriptions":["feed:match-42"]}
```

The operation is idempotent for the current connection. Overlong keys receive `subscription_key_too_long`; valid removals release quota and return:

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

`expiresAt` is `null` when no expiry was supplied. Routing targets are not exposed to clients. A connection matching both a direct user and one or more subscription keys receives one notification frame.

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
