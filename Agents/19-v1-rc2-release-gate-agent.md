# V1 RC.2 Release Gate Agent

## Mission

Reconcile final documentation, produce/validate `1.0.0-rc.2`, and execute the complete pre-stable release gate.

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


## Documentation Reconciliation

Update and verify:
- `README.md`
- `docs/architecture.md`
- `docs/message-source.md`
- `docs/delivery-semantics.md`
- `docs/ordering.md`
- `docs/configuration.md`
- `docs/protocol.md`
- `docs/testing.md`
- `docs/limitations.md`
- `docs/decision-log.md`
- `CHANGELOG.md`
- `RELEASE_NOTES.md`
- sample READMEs

Search the repository for stale:
- single-server claims
- fixed consumer-group IDs
- old routing-model references
- preview/runtime claims
- test-count claims
- configuration defaults

Compile or execute documented examples where practical.

## RC.2 Gate

Prepare `1.0.0-rc.2`.

Require:
- clean Release build
- zero warnings/errors per release policy
- all .NET tests, no hidden required skips
- JavaScript tests
- TypeScript check
- optimized Next.js build
- real multi-host producer -> Kafka -> WebSocket E2E from clean environment
- package inspection
- packed-package external smoke test
- Quality Scoring Agent in final-release mode
- score >= 85/100
- every mandatory gate PASS

Record evidence in:
- `docs/testing.md`
- `RELEASE_NOTES.md`

## Freeze

After PASS:
- freeze V1 public API
- freeze V1 wire protocol
- allow only release-blocking fixes before stable release

## PASS Gate

PASS means the exact RC.2 candidate commit/package is ready for prerelease feedback.

Do not authorize stable publication yourself.
