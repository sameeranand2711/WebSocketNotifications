# Development

V1 supports and tests .NET 10 only. The repository selects its SDK through `global.json`; install the current .NET 10 servicing update before building or deploying.

## Repository structure

```text
src/WebSocketNotifications/              provider-neutral library
tests/WebSocketNotifications.Tests/      behavior and integration tests
samples/WebSocketNotifications.Host/     Kafka-backed ASP.NET Core host
samples/NotificationProducer/             Kafka producer Web API
samples/clients/websocket-notifications-nextjs/  browser client and UI
scripts/                                  live end-to-end runner
Agents/                                   project workflow and review rules
```

Folders and namespaces are aligned by responsibility. Keep the project count small unless a real packaging or dependency boundary requires another assembly.

## Workflow

Core changes follow a cohesive test-driven slice:

```text
requirement -> failing behavior test -> minimal implementation -> focused test
            -> full suite -> refactor while green -> integration review
```

Do not weaken or skip a failing test. When a sample exposes a library defect, first reproduce it at the narrowest responsible layer, then change production code.

## Design priorities

Use this order when choosing a design:

```text
correctness -> clarity -> simplicity -> testability -> performance -> extensibility
```

- Add interfaces only at actual provider/application boundaries.
- Avoid wrapper chains, speculative factories, generic pipelines, distributed presence, and targeted server-routing infrastructure in V1.
- Preserve V1 source-level fan-out: every active server independently consumes cluster-wide notifications and performs local routing.
- Prefer direct branches and small concrete collaborators.
- Keep the public API small and treat it as compatibility-sensitive.
- Do not add a broker dependency to the core project.

The persistent detailed rules are in [`Agents/02-development-rules-agent.md`](../Agents/02-development-rules-agent.md). The sequential stable-release workflow is controlled by [`Agents/11-v1-release-orchestration-agent.md`](../Agents/11-v1-release-orchestration-agent.md). Each stage uses a dedicated branch and, after PASS, is merged only into the cumulative RC.2 branch. `main` remains unchanged until all pre-publication stages pass and the repository owner personally tests and approves the complete candidate.

## Async and resource rules

- Use async I/O end to end; no sync-over-async or detached tasks.
- Propagate cancellation through long-running operations.
- Bound every per-client or adapter channel.
- Keep exactly one sender and receiver per WebSocket.
- Never hold a registry lock while awaiting I/O or invoking application code.
- Observe task failures and make normal cancellation quiet.
- Use structured logging without payloads or credentials on normal hot paths.

## Configuration and secrets

Operational behavior belongs in .NET options, environment variables, or Next.js environment configuration. Internal protocol identifiers and safety ceilings may remain constants. Never commit real broker credentials, tokens, or long-lived browser secrets.

## Contribution checks

Before proposing a change:

```powershell
dotnet test WebSocketNotifications.slnx --configuration Release
dotnet build WebSocketNotifications.slnx --configuration Release --no-restore

cd samples/clients/websocket-notifications-nextjs
npm test
npm run typecheck
npm run build
```

Run `scripts/run-multi-host-e2e.ps1` for changes to serialization, queue adapters, source identity/readiness, hosting, WebSocket behavior, or the client.

## Review and scoring

Major phases use the review checklist in `Agents/02-development-rules-agent.md`. Core stabilization and final release are scored with `Agents/08-quality-scoring-agent.md`. V1 requires at least 85/100 and every mandatory gate; a numerical pass cannot override a routing, security, bounded-memory, test, E2E, or documentation gate failure.
