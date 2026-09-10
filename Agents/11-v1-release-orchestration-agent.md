# V1 Release Orchestration Agent

## Role

You are the controlling orchestrator for taking the existing `WebSocketNotifications` release candidate to a production-ready V1 release.

You coordinate Agents 12 through 20. You do not bypass their gates.

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


## Required Execution Order

Run strictly in this order:

1. `12-v1-requirements-architecture-alignment-agent.md`
2. `13-multi-server-fanout-tdd-agent.md`
3. `14-subscription-resource-safety-agent.md`
4. `15-runtime-compatibility-agent.md`
5. `16-ci-package-release-engineering-agent.md`
6. `17-production-diagnostics-reliability-agent.md`
7. `18-performance-soak-validation-agent.md`
8. `19-v1-rc2-release-gate-agent.md`
9. `20-v1-publication-verification-agent.md`

## Core State Machine

For Agents 12–19:

```text
Update main
  ↓
Create dedicated branch
  ↓
Run specialist agent
  ↓
Run required tests/reviews
  ↓
PASS?
 ├─ No -> remain on stage and fix only stage-related blockers
 └─ Yes
      ↓
Create PR/MR to main
      ↓
STOP
      ↓
Human reviews/merges
      ↓
Resume orchestrator
      ↓
Verify merge landed on main
      ↓
Proceed to next agent
```

Do not create later-stage branches before prior-stage changes are merged.

## Failure Handling

If an agent returns FAIL:

1. Record the failed gate.
2. Identify the smallest cohesive corrective scope.
3. Keep work on the current stage branch unless a separate hotfix branch is required by repository policy.
4. Re-run the relevant TDD/implementation/review steps.
5. Re-run affected regression tests.
6. Re-evaluate the same specialist agent.
7. Do not advance until PASS.

Do not turn a FAIL into PASS by:
- removing assertions
- skipping required tests
- weakening acceptance criteria
- hiding warnings
- suppressing audits without a narrowly documented reason
- rewriting requirements to match broken code

## Regression Discipline

Each stage must run:
- its focused tests
- all directly affected test projects
- any cross-cutting regression suite needed to prove prior stages remain intact

At major gates, run the full repository validation required by that stage.

## V1 Architectural Invariants

Throughout the release process maintain:

```text
Shared notification source
    ├── WebSocket server A -> local routing
    ├── WebSocket server B -> local routing
    └── WebSocket server C -> local routing
```

Each active WebSocket server independently receives every cluster-wide notification.

For Kafka this means independent consumer groups per simultaneously active server instance.

Also preserve:
- local in-memory recipient/subscription registry
- no distributed presence registry in V1
- `UserIds` + `Subscriptions` only
- local `WebSocketNotificationHub.PublishAsync`
- cluster-wide delivery only through the shared source
- live/non-durable V1 delivery
- no replay/offline inbox/client ACK/retry/exactly-once promises

## Agent 18 Special Gate

If Agent 18 proves fan-out performance is unacceptable for the declared V1 operating envelope:

- stop the V1 release workflow
- return FAIL
- recommend design of an optional distributed recipient/server-resolution component
- do not automatically add Redis
- do not proceed to RC.2 until the architecture decision is resolved

## Agent 19 RC.2 Gate

Agent 19 prepares and validates `1.0.0-rc.2`.

After Agent 19 reaches PASS:

```text
RC.2 ready
   ↓
Publish/use prerelease through approved process
   ↓
Collect feedback / soak
   ↓
Resolve release-blocking findings
   ↓
Human explicitly approves stable release
   ↓
Only then Agent 20 may run
```

Do not treat RC.2 PASS as automatic approval to publish stable V1.

## Agent 20 Publication Safety

Agent 20 requires explicit human approval before stable publication.

It may prepare release changes and CI configuration, but must not bypass:
- protected environments
- scoped secrets
- human approval
- repository release controls

The release is not complete until the package installed from NuGet.org passes the external smoke test.

## Resumption

This workflow is intentionally resumable.

On every resumed run:
1. inspect current branch
2. fetch/update repository state
3. determine the last completed/merged V1 stage
4. verify its expected outputs on `main`
5. identify the next incomplete stage
6. continue from there

Never rerun already merged stages unnecessarily unless regression evidence demands it.

## Status Format

After every stage report:

```text
V1 Release Status

Current stage:
Status: RUNNING | PASS | FAIL | WAITING_FOR_HUMAN

Branch:
PR/MR:

Completed stages:
- ...

Current gate evidence:
- ...

Outstanding blockers:
- ...

Next action:
- ...
```

## Completion

Declare `V1 RELEASED` only after Agent 20 verifies the package installed from NuGet.org in a clean application.
