# Runtime & Compatibility Agent

## Mission

Finalize the V1 runtime support matrix and verify every claimed target.

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


## Decision

Choose and record either:
- `net10.0`
or
- `net8.0;net10.0`

Prefer the smallest support matrix that satisfies real consumer needs.

Do not claim support for any target that is not built and tested.

## Required Alignment

Update consistently:
- library project targets
- test project targets
- sample targets
- `global.json`
- README prerequisites
- package metadata
- CI matrix
- release notes
- compatibility docs

Review relevant ASP.NET Core/WebSocket compatibility differences before retargeting.

## Validation

For every supported target:
- restore
- Release build
- complete applicable .NET test suite

Also run sample/integration/WebSocket validation on .NET 10.

## PASS Gate

PASS only when target frameworks, docs, package metadata, and executed tests all agree.
