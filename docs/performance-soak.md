# Performance and soak validation

Stage 18 records a repeatable V1 operating envelope for source-level fan-out. These results are evidence for this topology on this machine; they are not a universal throughput guarantee or a substitute for load testing an application's broker, network, authentication, payloads, and clients.

## Test environment

- Date: 2026-09-15
- OS: Microsoft Windows 10.0.26200, x64
- Runtime: .NET SDK 10.0.101 and Microsoft.NETCore.App 10.0.1
- Processor: AMD Ryzen 3 3250U, 2 cores and 4 logical processors
- Physical memory: 14,913,122,304 bytes (13.9 GiB)
- Build: Release, zero compiler warnings and errors

The load harness is a dependency-free, non-packable console project at `benchmarks/WebSocketNotifications.Performance`. It uses the library's actual connection registry, subscription indexes, notification router, dispatcher, bounded connection buffer, metrics, and WebSocket sender. Each simulated server owns an independent registry and receives the same source notification, matching the V1 source-level fan-out architecture.

The simulated WebSockets remove network and broker variance so the runs isolate local routing, buffering, sending, churn, and fan-out cost. The repository's live Kafka multi-host E2E separately proves independent consumer-group fan-out, broker-backed readiness, continued delivery after one host stops, Kafka stop/restart recovery, and no replay after restart.

## Recorded topology

The representative stress topology used:

- 1, 2, then 4 independently routed WebSocket servers
- 100 simultaneous connections per server
- 8 subscription associations per connection, including one shared routing key
- 50 source notifications per second, fanned out independently to every server
- 4,096-byte configured payload size
- 32-frame bounded outgoing buffer per connection
- 5% deliberately slow clients, each delayed 250 ms per send
- `DropCurrent` slow-client policy
- 5% connection churn every 10 seconds; replacement connections resubscribe
- one source pause at one-third of the run
- one last-server restart at two-thirds of the run
- graceful drain and disposal at the end

The normal-load control used four servers with the same connection, subscription, message-rate, payload, buffer, interruption, restart, and shutdown settings, but no slow clients or churn.

## Results

All rows passed routing, recipient accounting, send-failure, metric-cardinality, final queue, connection, and subscription invariants.

| Scenario | Run | Recipient sends | Slow-client drops | Shutdown/churn discards | Peak queue | Avg CPU | Peak working set | Final working set | Peak managed | Final managed | Allocated |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Normal control, 4 servers | 60 s | 1,139,600 | 0 | 0 | 0 | 4.42% | 54.05 MB | 45.35 MB | 5.61 MB | 0.63 MB | 492.86 MB |
| Stress, 1 server | 60 s | 268,765 | 15,974 | 161 | 255 | 6.30% | 50.54 MB | 44.85 MB | 4.39 MB | 0.48 MB | 147.93 MB |
| Stress, 2 servers | 60 s | 543,952 | 25,518 | 330 | 320 | 4.65% | 53.62 MB | 46.74 MB | 5.91 MB | 0.56 MB | 263.44 MB |
| Stress, 4 servers | 60 s | 1,088,267 | 51,073 | 660 | 640 | 8.23% | 54.78 MB | 47.32 MB | 7.94 MB | 0.64 MB | 487.54 MB |
| Stress soak, 4 servers | 600 s | 11,256,574 | 541,851 | 1,175 | 1,020 | 3.32% | 58.38 MB | 47.75 MB | 10.37 MB | 0.78 MB | 5.03 GB |

`Allocated` is cumulative allocation volume and is expected to grow with message count. Retained managed memory is represented by the post-shutdown `Final managed` measurement after a full collection. The soak processed 117,996 server dispatches and more than 11 million completed sends without retained connection/subscription/queue state or a send failure.

The slow-client rows intentionally contain drops: five clients per server can send only about four frames per second while the shared route targets them at about 50 frames per second. Routing remained non-blocking, the source rate stayed consistent as server count increased, and fast clients continued sending. Frames still queued when a churned connection or restarted server is deliberately stopped are reported separately as shutdown/churn discards rather than mislabeled as full-buffer drops.

Fan-out work scales with the number of active servers because every server independently receives and routes every notification. From one to four servers, completed recipient sends and cumulative allocations rose approximately linearly, as the architecture predicts. The four-server topology remained well within this test machine's CPU and memory capacity.

## Reproduce

Build first, then run:

```powershell
dotnet run --project benchmarks/WebSocketNotifications.Performance --configuration Release --no-build -- `
  --servers 4 `
  --connections-per-server 100 `
  --subscriptions-per-connection 8 `
  --message-rate 50 `
  --payload-bytes 4096 `
  --slow-client-percent 5 `
  --slow-send-delay-ms 250 `
  --buffer-capacity 32 `
  --churn-percent 5 `
  --churn-interval-seconds 10 `
  --duration-seconds 600 `
  --source-interruption-seconds 10 `
  --output artifacts/performance/result.json
```

The process exits nonzero unless all source dispatches, local matches, and recipient outcomes reconcile; no send fails; no high-cardinality metric tag appears; interruption and restart complete; and final queued, connection, and subscription gauges are zero.

## V1 conclusion and limits

PASS for the recorded V1 envelope of up to four active servers, 100 connections and 800 subscription associations per server, 50 shared-route notifications per second, 4 KiB payloads, and the documented slow-client/churn pressure. Source-level fan-out is acceptable for this declared envelope, so Stage 18 does not trigger design of a distributed recipient/server-resolution component.

The automated interactive run used a ten-minute sustained soak rather than a multi-hour deployment soak. Before stable publication, operators should run the same harness for multiple hours on representative production hardware and separately load test their real provider and network. Provider latency, authentication cost, TLS, actual WebSocket network throughput, browser processing, and cross-region behavior are outside this measurement.

## Development Rules review

- Required complexity: one internal queue-depth gauge and one non-packable measurement executable using the existing concrete routing components.
- Unnecessary complexity found: none. The harness has direct option parsing, collection, simulation, and reporting responsibilities without a framework or extensibility layer.
- Abstractions justified: the existing message-source and application boundaries remain unchanged; no production abstraction was added.
- Abstractions removed or avoided: no benchmark framework, socket strategy hierarchy, metrics wrapper interface, provider adapter, factory, or distributed-routing component was introduced.
- Concurrency concerns: queue depth uses atomic increments/decrements around the existing bounded multi-writer/single-reader channel; shutdown drains or explicitly discards remaining frames before final gauges are sampled.
- Performance concerns: queue accounting adds one atomic operation on enqueue and dequeue. The scale matrix showed no unacceptable cost in the declared envelope; fan-out and cumulative allocations rose approximately with routed work.
- Public API concerns: none. The new metric name is an operational contract; implementation and harness access remain internal.
- Recommended simplifications: none. Keep the harness dependency-free and keep provider/network capacity testing separate from core routing measurement.
