# V1 Publication & Verification Agent

## Mission

Promote the human-approved release candidate to `WebSocketNotifications` 1.0.0 and verify the published NuGet artifact.

## Shared Rules

These rules apply throughout the V1 release workflow.

- Read and obey the existing repository governance under `Agents/01-*.md` through `Agents/09-*.md` where applicable.
- Keep execution strictly sequential unless an agent explicitly identifies a safe, isolated subtask.
- Never begin the next V1 release stage until the current stage has produced all required outputs and reached PASS.
- Every change occurs on a dedicated publication branch created only after the approved cumulative RC.2 PR has landed on `main`.
- Never work directly on `main`.
- Never auto-merge publication changes. Human review and protected release controls remain mandatory.
- When resuming, update `main` and verify the approved cumulative candidate landed before creating the publication branch.
- Use TDD for behavior changes: cohesive failing tests first, minimal coherent implementation second, refactor while green.
- Preserve `UserIds` and `Subscriptions` as the only core routing inputs.
- Subscription keys remain opaque and application-defined.
- Do not add `ServerId`, `TenantId`, `PartnerId`, `Scope`, first-class group/feed/event models, or distributed presence into `NotificationEnvelope`.
- Keep Kafka/provider-specific dependencies out of the core NuGet package.
- Do not hide, skip, disable, or weaken required tests to obtain PASS.
- Treat warnings, audit failures, package validation failures, and flaky required tests as real release concerns.


## Preconditions

Do not run stable publication until:
- RC.2 passed
- prerelease feedback/soak is complete
- release-blocking feedback is resolved
- affected tests were repeated
- API/wire protocol remain frozen
- human explicitly approves stable publication

## Release Steps

- update versions/docs from RC suffix to `1.0.0`
- build final package from approved commit in CI
- tag exact approved commit as `v1.0.0`
- create GitHub release notes
- publish `.nupkg` and `.snupkg` through protected CI using scoped secrets
- verify NuGet page
- verify license
- verify README
- verify symbols
- verify repository link
- verify dependencies
- verify supported frameworks

## Published-Package Verification

Create/use a clean external application.

Install `WebSocketNotifications` version `1.0.0` from NuGet.org.

Repeat the external smoke test against the published package.

Repository build success alone is insufficient.

## Allowed Release Claims

Only announce guarantees actually validated.

Do not claim:
- exactly-once
- replay
- offline delivery
- distributed presence
- Redis-based server resolution
- global cross-server ordering
unless implemented and verified in a later version.

## PASS Gate

PASS only when the published NuGet.org package installs and passes the external smoke test.

Only then report:

```text
V1 RELEASED
```
