# V1 Limitations

V1 deliberately has a narrow operational model:

- Multi-server delivery uses all-node source-level fan-out
- In-memory, process-local connection and subscription tracking
- No distributed presence or targeted recipient-to-server resolution
- No Redis or targeted-routing backplane
- No multi-region coordination
- No replay
- No durable offline notification store
- No built-in client acknowledgement tracking
- No built-in WebSocket delivery retry
- No exactly-once guarantee
- No source-message deduplication
- JSON text messages only
- No binary protocol
- No tenant/scope model
- No subscription TTL

## Consequences

Every active WebSocket server must independently receive every cluster-wide notification. For Kafka, use an independent consumer group per simultaneously active server. A shared group load-balances each record to one server and produces incomplete routing because that server knows only its own sockets.

All-node fan-out repeats source consumption and local match work on every server. V1 does not optimize this with distributed presence or targeted per-server inboxes. If measured fan-out cost is unacceptable for the declared operating envelope, stable V1 must stop rather than silently claim scale-out readiness.

Clients may miss notifications during disconnection and must resubscribe after reconnect. The Next.js sample does this automatically but cannot recover messages published while it was offline.

An upstream at-least-once provider can create duplicate `MessageId` values at clients. Applications should make processing idempotent where necessary.

Socket-send completion does not prove application processing. Applications needing that guarantee must add domain acknowledgements and durable tracking outside the library.

The absence of a tenant or partner model does not prevent namespaced identities or subscriptions. Applications may resolve globally unique user IDs and authorize opaque keys such as `tenant:abc:group:premium`; the library does not parse or enforce those conventions.

## Deferred roadmap areas

A future version may define distributed presence and targeted per-server inboxes as an alternative to fan-out, tenant-aware subscription scopes, replay or offline storage, optional acknowledgement helpers, binary negotiation, subscription expiry, or multi-region routing. None of these is partially implemented or promised by V1.
