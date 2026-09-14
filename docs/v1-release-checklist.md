# V1 Release Checklist

This document lists the remaining work before publishing `WebSocketNotifications` 1.0.0. It is based on the code merged through pull request #1 and supersedes the earlier assumption that V1 supports only one WebSocket server.

## Current baseline

The merged `1.0.0-rc.1` code already provides:

- authenticated direct-user routing and authorized opaque subscriptions
- multiple connections per user within one server process
- bounded per-connection outgoing buffers and slow-client policies
- one send loop and one receive loop per connection
- heartbeat, compression, expiry, and message-size controls
- a provider-neutral `INotificationMessageSource`
- Kafka host and producer samples
- a reconnecting Next.js sample client
- automated .NET and JavaScript tests plus a live Kafka end-to-end runner
- MIT licensing, changelog, release notes, and implementation documentation

All existing verification must be rerun after the remaining changes. Historical RC results are useful evidence but are not final V1 release evidence.

## Branch workflow

Agents 12 through 19 accumulate sequentially on `release/v1.0.0-rc.2`. Each stage is developed on a new branch created from the latest cumulative commit, must reach PASS, and is then merged into the cumulative branch. Completed stage branches may be deleted after their merge is verified.

`main` remains unchanged until every stage through Agent 19 passes and the repository owner personally tests and approves the complete cumulative RC.2 candidate. Only then is one final PR created from the cumulative branch to `main`, and it is never automatically merged. Agent 20 still requires separate explicit approval for stable publication.

## V1 architecture decision: multi-server fan-out

V1 will support multiple WebSocket servers by delivering every notification to every active WebSocket server. Each server will use its existing in-memory registry to select its own matching connections.

```text
Notification topic
    +-- WebSocket server A -> local recipient routing
    +-- WebSocket server B -> local recipient routing
    +-- WebSocket server C -> local recipient routing
```

For Kafka, every simultaneously active WebSocket server must consume through a distinct consumer group. A shared consumer group load-balances a record to only one server and is therefore invalid for this fan-out topology.

This design deliberately does not add distributed connection or subscription state. It also does not require identifying one server for a user because a user can have connections on several servers.

The following invariants must remain true:

- `UserIds` and `Subscriptions` remain the only notification routing inputs.
- Subscription keys remain opaque strings whose meaning belongs to the application.
- No `ServerId`, `TenantId`, `PartnerId`, or `Scope` is added to `NotificationEnvelope`.
- Server-instance identity is adapter and deployment metadata, not notification data.
- `WebSocketNotificationHub.PublishAsync` remains local. An application requiring cluster-wide delivery must publish through the shared fan-out source.
- Delivery remains live and non-durable: V1 does not add replay, offline delivery, client acknowledgements, WebSocket retry, deduplication, or exactly-once guarantees.

## Release blockers

Every item in this section must be completed before publishing 1.0.0.

### 1. Align the V1 requirements

- [x] Update the repository agent instructions that currently declare multi-server operation out of scope.
- [x] Update the architecture decision log with the new fan-out decision. Preserve the old single-server decision as superseded history rather than silently deleting it.
- [x] Define the supported topology precisely: every active server receives every notification and performs local routing.
- [x] Define the consumer-instance lifecycle and restart behavior consistently with V1's no-replay semantics.
- [x] Define readiness: a server must not advertise itself as ready until its notification source is able to receive new records.
- [x] State that cluster-wide notifications must use the shared source; direct calls to the local hub do not cross server boundaries.

Done when there is one non-contradictory V1 requirement across the agent files, code documentation, samples, and public documentation.

### 2. Implement Kafka fan-out in the hosted sample

- [x] Give every concurrently running host instance a unique consumer group ID.
- [x] Introduce an explicit deployment/instance identity configuration mechanism. Generate an ephemeral identity only when that behavior is documented and appropriate for the sample.
- [x] Namespace group IDs by application and environment to prevent unrelated deployments from consuming each other's records.
- [x] Remove the fixed `websocket-notification-host` group ID as the scalable default.
- [x] Choose offset initialization and restart behavior that do not imply offline replay.
- [x] Ensure the source is consuming before the host reports readiness.
- [x] Keep Kafka configuration and implementation in the sample adapter; do not add a Kafka dependency to the core package.
- [x] Document equivalent semantics for other providers: one independent subscription per WebSocket server, such as a queue per server bound to a fan-out exchange.

Done when two host instances can run concurrently and one produced notification is independently processed by both instances.

### 3. Prove multi-server behavior with TDD

Implement the change in cohesive failing slices and keep each slice green before starting the next one.

- [x] Test two servers with the same user connected to both; each connection receives one copy.
- [x] Test users connected to different servers; each user receives the intended notification.
- [x] Test the same subscription on different servers; matching connections on both receive it.
- [x] Test a notification matching both a user's identity and a subscription; each physical connection receives only one copy.
- [x] Test that a server with no matching local connection performs no WebSocket send.
- [x] Test one server stopping or disconnecting without preventing delivery on another server.
- [x] Test server restart behavior and confirm that V1 does not unexpectedly replay notifications published while that server was offline.
- [x] Add a real Kafka two-host end-to-end scenario, not only mocked or in-memory tests.
- [x] Make the multi-host E2E runner deterministic, self-cleaning, and suitable for local and CI execution.

Done when the tests fail with the old shared-consumer-group configuration and pass with independent per-server consumption.

### 4. Add subscription resource limits

Subscription state is now bounded per connection, complementing the existing bounded outgoing queue.

- [x] Add a configurable maximum number of subscriptions per connection.
- [x] Add a configurable maximum subscription-key length.
- [x] Decide whether a separate maximum number of keys per subscribe/unsubscribe command is useful in addition to the existing inbound-message-size limit. It is not added because the inbound byte limit bounds parsing and the connection quota bounds state growth.
- [x] Validate all new settings and define conservative defaults and absolute ceilings.
- [x] Reject over-limit commands atomically without partially modifying registry state.
- [x] Verify duplicate subscriptions do not consume additional quota.
- [x] Verify unsubscribe and connection cleanup release all associated state.
- [x] Document the protocol error and configuration behavior.

Done when one client cannot grow connection/subscription memory without a configured bound.

### 5. Finalize runtime support

V1 targets `net10.0` only. .NET 10 is the active LTS line, while [.NET 8 reaches end of support on November 10, 2026](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core). Supporting only .NET 10 keeps the first stable release on one long-lived runtime and avoids carrying a second matrix that expires shortly after release.

- [x] Choose and record the V1 support matrix: `net10.0` only.
- [x] Build and run the full library test suite for every supported target framework.
- [x] Run sample and WebSocket integration tests on .NET 10.
- [x] Review relevant .NET/ASP.NET Core compatibility changes before changing target frameworks.
- [x] Verify `global.json` and update README requirements, package metadata, samples, release notes, and compatibility documentation consistently.
- [x] Create CI workflows with the same .NET 10 matrix in Agent 16.

Done when the package's target frameworks, documented support policy, and tested runtimes agree.

### 6. Add continuous integration

- [x] Add a pull-request workflow that restores, builds in Release mode, and runs all .NET tests.
- [x] Run JavaScript tests, TypeScript checking, and the optimized Next.js build.
- [x] Run `dotnet pack` and retain the package as a workflow artifact.
- [x] Add a separate multi-host live Kafka E2E workflow.
- [x] Fail CI on warnings, skipped required tests, package-validation failures, or audit failures.
- [x] Add dependency caching without caching generated build output in a way that can hide failures.
- [x] Require the relevant checks on pull requests to `main`.

Done when a clean GitHub runner can reproduce the release build and all required tests without undocumented manual setup.

### 7. Harden and validate the NuGet package

- [x] Add `PackageLicenseExpression` with `MIT` even though the repository already contains the license file.
- [x] Add the appropriate project/package URL, tags, and other discovery metadata.
- [x] Enable SDK-provided Source Link and publish repository metadata.
- [x] Produce a symbol package (`.snupkg`).
- [x] Enable deterministic/continuous-integration build metadata for release builds.
- [x] Enable NuGet audit in CI and remove the repository-wide `NuGetAudit=false` suppression.
- [x] Enable package validation for the release package.
- [ ] Establish `1.0.0` as the API-compatibility baseline for subsequent releases after stable V1 is published.
- [x] Inspect the generated `.nupkg` and `.snupkg` contents.
- [x] Install the packed package into a clean external ASP.NET Core smoke-test application and verify registration, endpoint mapping, authentication/identity integration, and a real WebSocket exchange.
- [x] Confirm that the core NuGet package has no Kafka, Redis, sample, frontend, or other package dependency.

Done when the exact package intended for publication has been built once in CI, inspected, and consumed successfully without project references.

### 8. Add minimum production diagnostics

- [x] Add low-overhead metrics for active connections and active subscriptions.
- [x] Count notifications received, expired, locally matched, locally unmatched, enqueued, dropped, and rejected as oversized.
- [x] Count slow-client drops and disconnections.
- [x] Count accepted, denied, invalid, and over-limit subscription commands.
- [x] Avoid high-cardinality dimensions such as raw user IDs, connection IDs, and subscription keys.
- [x] Expose notification-source readiness in the sample host.
- [x] Document metric names and the difference between routing acceptance and WebSocket send completion.

Done when an operator can distinguish an idle system from a disconnected source, unmatched routing, buffer pressure, and failed WebSocket delivery without enabling debug logs.

### 9. Perform security and reliability review

- [x] Re-run the Development Rules review after the multi-server and resource-limit changes.
- [x] Confirm that direct user identity still comes only from the authenticated request.
- [x] Confirm that subscription authorization is invoked before any state mutation.
- [x] Confirm that multi-server changes do not expose server IDs or presence information to clients.
- [x] Review compression guidance for sensitive payloads.
- [x] Verify malformed, fragmented, binary, oversized, and rapidly repeated control messages cannot create unbounded state.
- [x] Verify cancellation, source failure, connection shutdown, and application shutdown observe all owned tasks and release resources.
- [x] Review logs to ensure payloads, tokens, and raw user-controlled values are not logged unnecessarily.

Done when no Critical or High routing, delivery, authentication, authorization, concurrency, or resource-safety defect remains.

### 10. Run final performance and soak validation

- [ ] Establish and record a representative test topology, including server count, connections per server, subscription count, message rate, payload size, and slow-client percentage.
- [ ] Measure the fan-out cost as server count increases.
- [ ] Verify that local routing remains non-blocking when another server or client is slow.
- [ ] Run a multi-hour soak covering connection churn, resubscription, broker interruption, server restart, and graceful shutdown.
- [ ] Record CPU, memory, allocation, queue-depth, drop, and disconnect observations.
- [ ] Define the tested operating envelope without presenting it as a universal throughput guarantee.

Done when the chosen V1 topology has repeatable evidence of stable memory and correct delivery under representative sustained load.

### 11. Update all documentation after the code is stable

- [ ] Update `README.md` to describe multi-server fan-out and remove the single-server limitation.
- [ ] Update `docs/architecture.md` with the cross-server flow and local-routing boundary.
- [ ] Update `docs/message-source.md` with the independent-subscription requirement.
- [ ] Update `docs/delivery-semantics.md` and `docs/ordering.md` for multi-server processing and the absence of cross-server global ordering.
- [ ] Update `docs/configuration.md` with instance/group configuration, subscription limits, readiness, and all exact defaults.
- [ ] Update `docs/protocol.md` with any new limit error while keeping the wire model generic.
- [ ] Update `docs/testing.md` with final commands and fresh results.
- [ ] Update `docs/limitations.md`; remove resolved single-server statements and retain genuine limitations.
- [ ] Update `docs/decision-log.md`, `CHANGELOG.md`, `RELEASE_NOTES.md`, and every sample README.
- [ ] Search the repository for stale `single server`, fixed consumer-group, old routing-model, preview-version, test-count, and configuration claims.
- [ ] Compile or execute documented examples where practical and verify every referenced path.

Done when documentation describes only the behavior present in the final release commit.

### 12. Execute the final release gate

- [ ] Create `1.0.0-rc.2` after the preceding implementation changes rather than publishing stable directly from RC.1.
- [ ] Publish RC.2 to prerelease consumers and allow an agreed soak/feedback period.
- [ ] Resolve all release-blocking RC feedback and repeat affected tests.
- [ ] Run the complete Release build and confirm zero warnings and errors.
- [ ] Run all .NET tests with no hidden skips.
- [ ] Run JavaScript tests, TypeScript checking, and the optimized client build.
- [ ] Run the real multi-host producer-to-Kafka-to-WebSocket E2E test from a clean environment.
- [ ] Run the Quality Scoring Agent in final-release mode.
- [ ] Require at least 85/100 and every mandatory gate to pass; the score alone is not sufficient.
- [ ] Record the final evidence in `docs/testing.md` and `RELEASE_NOTES.md`.
- [ ] Freeze the V1 public API and wire protocol after the gate passes.

Done when the exact candidate commit and package have passed every mandatory gate and have no unresolved release-blocking issue.

### 13. Publish and verify 1.0.0

- [ ] Change package and documentation versions from the RC suffix to `1.0.0`.
- [ ] Build the final package from the approved commit in CI.
- [ ] Tag that exact commit as `v1.0.0`.
- [ ] Create GitHub release notes from the finalized changelog/release notes.
- [ ] Publish the `.nupkg` and `.snupkg` to NuGet.org using a protected CI environment and scoped secret.
- [ ] Verify the NuGet page, license, README, symbols, repository link, dependencies, and supported frameworks.
- [ ] Install `WebSocketNotifications` 1.0.0 from NuGet.org into a clean application and repeat the smoke test.
- [ ] Announce only the guarantees actually verified by the release gate.

V1 is released only after the published artifact, not merely the repository build, passes the final installation smoke test.

## Explicitly deferred beyond V1

The following are valuable future features but are not required by the selected V1 fan-out architecture:

- a Redis or other distributed user/subscription-to-server presence registry
- targeted per-server inboxes for avoiding all-node fan-out
- replay and durable offline notification storage
- application acknowledgement helpers and redelivery
- exactly-once or source-level deduplication claims
- multi-region presence and routing
- first-class tenant, partner, role, feed, event, or group models
- binary protocol and capability negotiation
- subscription TTLs
- reusable published JavaScript/TypeScript and .NET client packages

If fan-out performance is unacceptable under the recorded V1 load test, targeted distributed routing becomes a release blocker. It must then be designed as a separate, optional scale-out component while preserving the neutral envelope and local routing core.

## Recommended execution order

1. Align requirements and record the fan-out decision.
2. Implement the Kafka/sample fan-out behavior with TDD.
3. Add subscription bounds.
4. Finalize runtime targets.
5. Add CI, package hardening, diagnostics, and readiness.
6. Run multi-server functional, performance, security, and soak validation.
7. Update documentation and publish RC.2.
8. Collect RC feedback and run the final release gate.
9. Publish and independently verify 1.0.0.

## External release references

- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
- [.NET library versioning guidance](https://learn.microsoft.com/en-us/dotnet/standard/library-guidance/versioning)
- [NuGet package guidance](https://learn.microsoft.com/en-us/dotnet/standard/library-guidance/nuget)
- [NuGet package compatibility rules](https://learn.microsoft.com/en-us/dotnet/standard/library-guidance/nuget-package-compatibility-rules)
- [.NET metrics instrumentation](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation)
