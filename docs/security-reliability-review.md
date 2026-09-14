# V1 security and reliability review

Stage 17 reviewed the cumulative multi-server, subscription-limit, runtime, and package changes against the V1 threat and lifecycle boundaries. No Critical or High defect remains in the reviewed routing, delivery, authentication, authorization, concurrency, lifecycle, or resource-safety paths.

## Findings and evidence

- Direct routing identity is produced only by `IWebSocketUserResolver` after ASP.NET Core authorization. The client protocol has no command that mutates `UserIds`.
- Every requested subscription key is authorized before the registry is mutated. Batch denial remains atomic, and unsubscribe cannot add routing state.
- The notification wire frame contains delivery data only. It does not expose `UserIds`, `Subscriptions`, server identity, connection presence, or source-provider metadata.
- Compression remains disabled by default. Enabling it is an application decision because compressed attacker-controlled data sharing a compression context with secrets can create side-channel risk.
- Fragmented text is assembled only up to `MaxIncomingMessageSize`; binary and oversized input closes the socket. Per-connection subscription state, subscription-key length, and outgoing queues are bounded. Duplicate subscriptions are idempotent.
- Sender, receiver, and heartbeat tasks are owned by one connection session. Completion or failure of one cancels and awaits its siblings, completes the buffer, and removes registry state. Source cancellation and non-cancellation failure propagate to the host boundary.
- Metrics contain no user-controlled dimensions. Core and sample logs do not log payloads, tokens, subscription keys, or raw user IDs; operational connection logs use only the library-generated connection ID.
- Source-level fan-out remains provider-owned. No server identity or provider dependency was added to the core envelope or package.

The focused tests cover authenticated identity resolution, authorization-before-mutation, atomic limits, malformed/fragmented/binary/oversized messages, slow-client policies, send failure, source cancellation/failure, connection cleanup, and multi-host local routing. The real Kafka E2E additionally covers independent consumer groups, host loss, restart, and no replay.
