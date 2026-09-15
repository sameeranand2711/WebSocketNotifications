# Metrics and operational signals

The library emits low-overhead instruments through the `WebSocketNotifications` .NET `Meter`. It does not select or bundle an exporter; applications can collect the meter with `MeterListener`, OpenTelemetry, or their existing observability stack.

V1 emits no metric tags. In particular, user IDs, connection IDs, subscription keys, payload values, and other high-cardinality or sensitive values are never metric dimensions.

| Instrument | Type | Meaning |
|---|---|---|
| `websocket_notifications.connections.active` | observable gauge | Current locally registered WebSocket connections |
| `websocket_notifications.subscriptions.active` | observable gauge | Current local connection-to-subscription associations; the same key on two connections counts twice |
| `websocket_notifications.notifications.received` | counter | Notifications accepted by this server's dispatcher, whether published locally or received from the configured source |
| `websocket_notifications.notifications.expired` | counter | Received notifications rejected because their expiry is at or before dispatch time |
| `websocket_notifications.notifications.matched` | counter | Non-expired notifications resolving to at least one local connection before serialization and enqueue |
| `websocket_notifications.notifications.unmatched` | counter | Non-expired notifications resolving to no local connection |
| `websocket_notifications.messages.enqueued` | counter | Outgoing frames accepted by a connection buffer, including notifications, protocol responses, and heartbeat pings |
| `websocket_notifications.messages.queued` | observable gauge | Current frames waiting across local connection buffers; frames being sent are no longer queued |
| `websocket_notifications.messages.dropped` | counter | Outgoing frames discarded by a full connection buffer, including frames displaced by `DropOldest` |
| `websocket_notifications.messages.rejected_oversized` | counter | Incoming control messages or serialized outgoing notifications rejected by their configured size limit |
| `websocket_notifications.clients.slow_drops` | counter | Full-buffer events handled by `DropCurrent` or `DropOldest` |
| `websocket_notifications.clients.slow_disconnects` | counter | Full-buffer events handled by `Disconnect` |
| `websocket_notifications.messages.sent` | counter | Buffered frames whose WebSocket `SendAsync` operation completed successfully |
| `websocket_notifications.messages.send_failures` | counter | Buffered frames whose WebSocket send failed for a non-cancellation reason |
| `websocket_notifications.subscriptions.commands.accepted` | counter | Valid subscribe or unsubscribe commands accepted after authorization and limits; idempotent commands count as accepted |
| `websocket_notifications.subscriptions.commands.denied` | counter | Subscribe commands rejected by the application authorizer |
| `websocket_notifications.subscriptions.commands.invalid` | counter | Subscribe or unsubscribe commands with a missing or malformed `subscriptions` array |
| `websocket_notifications.subscriptions.commands.over_limit` | counter | Subscribe or unsubscribe commands rejected by the connection quota or key-length limit |

Routing and enqueue are not delivery acknowledgements. A notification can be locally matched but fail outgoing-size validation, and an enqueued frame can later be dropped by `DropOldest` or fail during WebSocket send. Use `messages.sent` only as evidence that the server-side send operation completed; it does not prove that client application code processed the frame.

The sample host exposes Kafka assignment-aware readiness at `/health/ready`: HTTP 200 means the source is running with a partition assignment, while HTTP 503 means it is disconnected or unready. Together, readiness and metrics distinguish common states:

- healthy readiness with no increase in `notifications.received`: an idle source
- failing readiness: a disconnected or unassigned source
- increasing `notifications.unmatched`: notifications received with no local route
- increasing slow-client or dropped counters: connection-buffer pressure
- increasing `messages.send_failures`: failed or closed WebSockets
