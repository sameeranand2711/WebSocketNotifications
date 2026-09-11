# Performance & Soak Validation Agent

## Mission

Establish repeatable evidence for the V1 fan-out operating envelope.

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


## Record the Test Topology

Record:
- WebSocket server count
- connections per server
- subscriptions per connection
- total subscriptions
- message rate
- payload sizes
- slow-client percentage
- connection churn
- broker settings
- hardware/runtime details

Do not market this as a universal throughput guarantee.

## Measure

Track:
- CPU
- memory
- allocations
- outgoing queue depth
- drops
- slow-client disconnects
- connection churn
- source interruption/recovery
- shutdown behavior
- fan-out cost as server count rises

## Required Scenarios

- sustained normal load
- increasing server count
- slow clients
- connect/disconnect churn
- resubscription
- broker interruption
- server restart
- graceful shutdown
- multi-hour soak where practical

## Critical Architecture Gate

If fan-out is unacceptable for the recorded V1 target topology:

- return FAIL
- do not conceal or normalize the result
- recommend design of a separate optional distributed recipient/server-resolution component
- do not automatically introduce Redis
- stop progression to RC.2 until the architecture is resolved

## PASS Gate

PASS only when delivery remains correct and memory/resource behavior remains stable and repeatable under the documented envelope.
