# Subscription Resource-Safety Agent

## Mission

Bound per-connection subscription memory and harden subscription commands against abuse.

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


## Required Configuration

Add:
- configurable maximum subscriptions per connection
- configurable maximum subscription-key length

Evaluate, but do not automatically add, a maximum number of keys per subscribe/unsubscribe command. Add it only if it materially improves safety beyond the existing inbound-message-size limit.

All configurable limits require:
- conservative defaults
- absolute hard ceilings
- startup validation
- clear protocol error behavior

Invalid unsafe configuration must fail clearly, not silently clamp.

## Required Semantics

- over-limit commands are rejected atomically
- no partial registry mutation
- duplicate subscriptions do not consume additional quota
- unsubscribe releases state
- disconnect cleanup releases state
- authorization runs before mutation
- denied/invalid requests do not consume quota

## TDD

Cover:
- exactly at limit
- one above limit
- key length at/above limit
- duplicates
- mixed valid/invalid keys
- atomic rejection
- unsubscribe then re-subscribe
- disconnect cleanup
- invalid settings
- absolute ceiling enforcement

## PASS Gate

PASS only when one authenticated connection cannot grow subscription memory without a configured bound.
