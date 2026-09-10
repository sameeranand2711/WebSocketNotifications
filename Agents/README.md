# WebSocketNotifications V1 Release Agent Pack

This pack supplements the existing repository Agents 01–10.

## Numbering

- 11 — V1 Release Orchestration
- 12 — Requirements & Architecture Alignment
- 13 — Multi-Server Fan-Out TDD
- 14 — Subscription Resource Safety
- 15 — Runtime & Compatibility
- 16 — CI & Package Release Engineering
- 17 — Production Diagnostics & Reliability
- 18 — Performance & Soak Validation
- 19 — V1 RC.2 Release Gate
- 20 — V1 Publication & Verification

`11-v1-release-orchestration-agent.md` is the controlling bootstrap for this workflow.

## Important workflow

Each specialist stage:

```text
updated main
  -> dedicated branch
  -> implementation/validation
  -> PASS
  -> PR/MR to main
  -> STOP / human review
  -> human merge
  -> resume orchestrator
```

No automatic merge.

Stable 1.0.0 publication requires explicit human approval after RC.2 feedback/soak, and the release is complete only after the NuGet.org package passes a clean external smoke test.

## V1 architecture

V1 uses source-level fan-out to multiple WebSocket servers. Each server independently consumes each cluster-wide notification and performs local routing using its in-memory registry.

Core routing remains:
- `UserIds`
- `Subscriptions`

Distributed presence/targeted server resolution (for example Redis-backed resolution) is deferred unless Agent 18 proves V1 fan-out inadequate.
