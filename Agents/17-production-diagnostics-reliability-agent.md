# Production Diagnostics & Reliability Agent

## Mission

Add minimum V1 observability and complete a focused security/reliability review.

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


## Metrics

Add low-overhead metrics for:
- active connections
- active subscriptions
- notifications received
- expired
- locally matched
- locally unmatched
- enqueued
- dropped
- rejected oversized
- slow-client drops
- slow-client disconnects
- accepted subscription commands
- denied subscription commands
- invalid subscription commands
- over-limit subscription commands

Avoid high-cardinality dimensions:
- user IDs
- connection IDs
- subscription keys
- payload values

Document metric names and semantics.

Clearly distinguish routing/enqueue acceptance from WebSocket send completion.

## Readiness

Expose source readiness in the sample host.

Operators must be able to distinguish:
- idle system
- disconnected/unready source
- no local route match
- buffer pressure
- failed/closed WebSocket

## Security/Reliability Review

Verify:
- direct identity comes only from authenticated request/application resolver
- subscription authorization precedes mutation
- server IDs/presence data are not exposed to clients
- compression guidance addresses sensitive payload concerns
- malformed/fragmented/binary/oversized/repeated control messages cannot create unbounded state
- cancellation/source failure/connection shutdown/app shutdown observe owned tasks
- resources are released
- logs avoid unnecessary payloads/tokens/raw user-controlled values

## PASS Gate

PASS only when no Critical or High defect remains in routing, delivery, authn/authz, concurrency, lifecycle, or resource safety.
