# Ordering

The library preserves order within the ordering domain supplied to it; it does not manufacture a global order.

## Core path

The registered message source invokes and awaits one notification handler in source order. Routing synchronously offers each notification to recipient buffers. Each connection has one FIFO channel reader and exactly one WebSocket sender, preserving accepted buffer order for that connection.

Slow-client policies affect observable completeness:

- `Disconnect` ends the connection on overflow.
- `DropOldest` preserves the order of the messages that remain but removes an older item.
- `DropCurrent` preserves the existing queue and discards the current item.

No policy invents a new order, but drop policies create gaps.

## Kafka ordering domain

The producing application chooses the Kafka key. A key such as `user:user-123`, `feed:match-42`, or `order:12345` gives related records partition affinity and therefore an application-defined ordering domain. The hosted sample consumes with ordered-by-partition processing.

The WebSocket library never generates or interprets Kafka keys and the neutral notification envelope contains no key or server ID.

Kafka does not define a relative global order across different partitions or unrelated keys. Consequently, the library cannot promise an order between those streams. If two independently processed streams target the same connection concurrently, their interleaving is the order in which the source adapter submits them.

## Multiple clients

Each connection has its own buffer and sender. Two connections for the same user receive direct notifications in source-routing order, but their network completion times are independent. No ordering guarantee compares delivery completion across clients.
