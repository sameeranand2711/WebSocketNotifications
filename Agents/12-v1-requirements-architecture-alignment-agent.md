# V1 Requirements & Architecture Alignment Agent

## Mission

Make the repository internally consistent with the selected V1 multi-server fan-out architecture before further implementation work begins.

## Shared Rules

These rules apply throughout the V1 release workflow.

- Read and obey the existing repository governance under `Agents/01-*.md` through `Agents/09-*.md` where applicable.
- Keep execution strictly sequential unless an agent explicitly identifies a safe, isolated subtask.
- Never begin the next V1 release stage until the current stage has produced all required outputs and reached PASS.
- Every change occurs on a dedicated stage branch created from the latest `release/v1.0.0-rc.2` cumulative branch.
- Never work directly on `main`.
- After PASS, merge the stage branch into the cumulative branch and verify it landed before proceeding.
- Do not create a PR/MR to `main` until Agents 12 through 19 pass and the human personally tests and approves the complete cumulative candidate. Never auto-merge that final PR/MR.
- When resuming, update the cumulative branch, verify the previous stage landed, and branch from that state.
- Use TDD for behavior changes: cohesive failing tests first, minimal coherent implementation second, refactor while green.
- Preserve `UserIds` and `Subscriptions` as the only core routing inputs.
- Subscription keys remain opaque and application-defined.
- Do not add `ServerId`, `TenantId`, `PartnerId`, `Scope`, first-class group/feed/event models, or distributed presence into `NotificationEnvelope`.
- Keep Kafka/provider-specific dependencies out of the core NuGet package.
- Do not hide, skip, disable, or weaken required tests to obtain PASS.
- Treat warnings, audit failures, package validation failures, and flaky required tests as real release concerns.


## Required V1 Topology

```text
Notification source
    ├── WebSocket server A -> local recipient routing
    ├── WebSocket server B -> local recipient routing
    └── WebSocket server C -> local recipient routing
```

Every simultaneously active WebSocket server independently receives every notification intended for cluster-wide delivery.

For Kafka, every active WebSocket server uses an independent consumer group. A shared consumer group is invalid because it load-balances records instead of fanning them out.

## Required Work

Inspect and align:
- repository agent instructions
- architecture docs
- decision log
- README
- sample host docs/config
- comments describing single/multi-server behavior
- readiness semantics
- restart/no-replay semantics
- local-vs-cluster publishing semantics

Update any agent instruction that still declares multi-server operation out of scope.

Preserve the old single-server decision in history as superseded rather than silently deleting it.

## V1 Invariants

Document clearly:
- `UserIds` and `Subscriptions` are the only routing inputs.
- Subscription keys are opaque strings owned by the application.
- No distributed connection/subscription state exists in V1.
- No `ServerId`, `TenantId`, `PartnerId`, or `Scope` is added to the envelope.
- Server instance identity is adapter/deployment metadata.
- `WebSocketNotificationHub.PublishAsync` remains process-local.
- Cluster-wide notification delivery must use the shared fan-out source.
- No replay, offline delivery, client ACK, WebSocket retry, deduplication, or exactly-once guarantee.
- Readiness means the host's source is capable of receiving new records.
- Restart behavior must not imply replay of notifications emitted while that server was offline.

## Repository Search

Search for stale phrases/config including:
- `single server`
- `single WebSocket server`
- `multi-server out of scope`
- fixed consumer-group IDs
- shared group assumptions
- old `Groups`, `Feeds`, or `EventTypes` routing references

## PASS Gate

PASS only when agent instructions, architecture docs, samples, and public docs describe one non-contradictory V1 architecture.

Report:
- files changed
- old decisions superseded
- final topology
- readiness semantics
- restart semantics
- local vs cluster-wide publishing
- unresolved contradictions
