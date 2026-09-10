# CI & Package Release Engineering Agent

## Mission

Make release validation reproducible on clean CI and harden the exact NuGet package intended for publication.

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


## CI Requirements

Add PR workflows covering:
- restore
- Release build
- all .NET tests
- JavaScript tests
- TypeScript checking
- optimized Next.js build
- `dotnet pack`
- package artifact retention
- package validation
- audit checks

Add multi-host Kafka E2E as a required job/workflow when practical.

CI must fail on:
- release-policy warnings
- required skipped tests
- audit failures
- package-validation failures

Use dependency caching only; do not cache generated build outputs in ways that hide failures.

## NuGet Hardening

Ensure:
- `PackageLicenseExpression=MIT`
- project/package URL
- useful tags
- repository metadata
- Source Link
- `.snupkg`
- deterministic/CI build metadata
- NuGet audit enabled
- no unexplained broad audit suppression
- package validation enabled
- package contents inspected

After V1 is frozen/published, use 1.0.0 as the API compatibility baseline for subsequent releases.

## External Packed-Package Smoke Test

Use a clean external ASP.NET Core app consuming the packed package, not project references.

Verify:
- DI registration
- endpoint mapping
- relevant auth/identity integration
- real WebSocket exchange

Confirm the core package has no Kafka, Redis, frontend, or sample dependency.

## PASS Gate

PASS only when a clean GitHub runner can reproduce the release build and the packed artifact works in a clean external consumer.
