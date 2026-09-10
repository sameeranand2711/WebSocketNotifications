# Multi-Server Fan-Out TDD Agent

## Mission

Implement and prove source-level fan-out to multiple WebSocket servers.

## Shared Rules

These rules apply throughout the V1 release workflow.

- Read and obey the existing repository governance under `Agents/01-*.md` through `Agents/09-*.md` where applicable.
- Keep execution strictly sequential unless an agent explicitly identifies a safe, isolated subtask.
- Never begin the next V1 release stage until the current stage has produced all required outputs and reached PASS.
- Every implementation, fix, hotfix, refactor, release-engineering change, or documentation change must occur on a newly created and checked-out dedicated branch.
- Never work directly on `main`.
- After a stage passes, create a pull/merge request targeting `main`.
- Never auto-merge. Human review decides whether to merge.
- When resuming after a human merge, first update local `main`, verify the previous stage landed, then create the next branch from the updated `main`.
- Use TDD for behavior changes: cohesive failing tests first, minimal coherent implementation second, refactor while green.
- Preserve `UserIds` and `Subscriptions` as the only core routing inputs.
- Subscription keys remain opaque and application-defined.
- Do not add `ServerId`, `TenantId`, `PartnerId`, `Scope`, first-class group/feed/event models, or distributed presence into `NotificationEnvelope`.
- Keep Kafka/provider-specific dependencies out of the core NuGet package.
- Do not hide, skip, disable, or weaken required tests to obtain PASS.
- Treat warnings, audit failures, package validation failures, and flaky required tests as real release concerns.


## Scope

Implement/test:
- unique source subscription/consumer identity per active server
- Kafka independent consumer groups
- application/environment namespacing
- explicit instance/deployment identity configuration
- documented ephemeral identity fallback if appropriate
- source readiness
- restart/no-replay behavior
- deterministic two-host Kafka E2E

Keep Kafka code entirely outside the core package.

## TDD Scenarios

Create cohesive failing slices for:

1. Same user connected to two servers -> each physical connection receives one copy.
2. Different users connected to different servers -> intended users receive.
3. Same subscription present on multiple servers -> matches on all servers receive.
4. A physical connection matching both `UserIds` and `Subscriptions` -> one WebSocket send only.
5. Server with no local match -> no WebSocket send.
6. One server stops/disconnects -> another continues.
7. Server restart -> no unexpected replay of messages emitted while offline.
8. Real Kafka two-host scenario -> shared consumer group fails fan-out expectations; independent groups pass.

## Kafka Requirements

- Remove fixed scalable-default group IDs.
- Namespace groups by application and environment.
- Ensure concurrently active hosts have distinct group IDs.
- Define offset initialization consistently with live/no-replay V1 semantics.
- Do not report host ready until source consumption is operational.

Document equivalent semantics for other providers (e.g. independent queue/subscription per server bound to a fan-out source).

## E2E Runner

Must be:
- deterministic
- self-cleaning
- repeatable locally
- CI-suitable
- explicit about dependencies
- able to detect readiness/startup failure

## PASS Gate

PASS only when:
- focused tests pass
- affected regression tests pass
- real two-host Kafka E2E passes
- core package remains provider-neutral
- readiness is verified

Report implementation, tests, group-ID scheme, readiness behavior, E2E evidence, and remaining risks.
