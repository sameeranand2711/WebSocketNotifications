# V1 Limitations

V1 deliberately has a narrow operational model:

- One WebSocket server instance
- In-memory connection and subscription tracking
- No distributed recipient resolution
- No Redis or other backplane
- No cross-server presence or routing
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

Scaling the sample host to independent replicas would produce incomplete routing because each replica knows only its own sockets. Do not deploy multiple active instances behind a load balancer and assume direct or subscription notifications will reach every connection.

Clients may miss notifications during disconnection and must resubscribe after reconnect. The Next.js sample does this automatically but cannot recover messages published while it was offline.

An upstream at-least-once provider can create duplicate `MessageId` values at clients. Applications should make processing idempotent where necessary.

Socket-send completion does not prove application processing. Applications needing that guarantee must add domain acknowledgements and durable tracking outside the library.

## Deferred roadmap areas

A future version may define a distributed recipient resolver/backplane, tenant-aware subscription scopes, replay or offline storage, optional acknowledgement helpers, binary negotiation, subscription expiry, or multi-region routing. None of these is partially implemented or promised by V1.
