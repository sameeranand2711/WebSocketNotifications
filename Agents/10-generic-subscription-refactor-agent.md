# Generic Subscription Refactor Agent

# Role

You are the focused refactor agent responsible for simplifying the WebSocket Notification Library routing model before V1 is finalized.

The current implementation contains redundant routing concepts:

- `NotificationKind`
- `Groups`
- `Feeds`
- `EventTypes`

The target model should keep only:

- direct recipients via `UserIds`
- generic subscription routing via `Subscriptions`

The consuming application owns the semantic meaning of subscription keys.

This refactor MUST reduce complexity.

Do not replace the old concepts with a more elaborate generic framework.

# Governing Rules

Before changing code, read and obey:

- `Agents/01-orchestration-agent.md`
- `Agents/02-development-rules-agent.md`
- `Agents/03-tdd-test-agent.md`
- `Agents/04-library-implementation-agent.md`

Treat `02-development-rules-agent.md` as persistent governing rules.

This change follows TDD. Do not begin by rewriting production code.

# Refactor Goal

Remove redundant category-specific routing concepts from the core library where they do not provide technically distinct behavior.

The core routing model should become:

```text
Direct Recipients:
    UserIds

Generic Subscription Routing:
    Subscriptions
```

The library must treat `Subscriptions` values as opaque strings.

The library must not interpret whether a subscription means:
- group
- feed
- event
- role
- tenant
- partner
- market
- product
- channel
- application-specific topic

Those meanings belong entirely to the consuming application.

# Target Notification Contract

The notification envelope should conceptually support:

```csharp
public sealed record NotificationEnvelope
{
    public required string MessageId { get; init; }
    public IReadOnlyCollection<string>? UserIds { get; init; }
    public IReadOnlyCollection<string>? Subscriptions { get; init; }
    public required JsonElement Payload { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}
```

Use the actual existing payload representation if it is already appropriate.

Do not change unrelated public contract members merely because this refactor is happening.

# Required Investigation

Before modifying anything, inspect all usages of:

```text
NotificationKind
Groups
Feeds
EventTypes
```

Find all affected:
- public contracts
- internal models
- routing logic
- recipient resolution
- connection/subscription registry
- subscription authorization
- protocol request/response types
- validators
- serialization
- tests
- sample applications
- documentation

For each usage, determine whether it represents genuinely distinct behavior or only category labeling.

For `NotificationKind`, ask:

```text
What decision becomes impossible if NotificationKind disappears?
```

If the answer is "none", remove it.

# Direct Users Stay Separate

Do NOT merge `UserIds` into `Subscriptions`.

Direct-user routing has distinct security semantics.

The authenticated connection identity determines direct-user association.

Clients must not be able to subscribe as another user.

Preserve the existing user resolver behavior.

Client subscription commands control only generic `Subscriptions`.

# Generic Subscription Model

Applications may use conventions such as:

```text
group:premium
feed:football-live
event:order.created
tenant:abc:group:premium
partner:p1:event:deposit.completed
```

These prefixes are examples only.

The library MUST NOT:
- require them
- parse them
- validate their semantic structure
- add separate enums for them
- add separate routing branches for them

To the core library, each value is simply:

```text
subscriptionKey : string
```

# Multi-Tenant and Multi-Partner Responsibility

The core library remains tenant-agnostic and partner-agnostic.

Do NOT add first-class V1 properties such as:

```text
TenantId
PartnerId
Scope
```

Applications are responsible for namespacing and authorizing routing identifiers.

Examples:

```text
tenant:abc:user:123
tenant:abc:group:premium
partner:p1:feed:orders
tenant:abc:partner:p1:event:deposit.completed
```

The application-provided user resolver may return a globally unique routing identity such as:

```text
tenant:abc:user:123
```

when raw user IDs are not globally unique.

# Protocol Simplification

Replace category-specific subscription protocol fields.

Old conceptual request:

```json
{
  "type": "subscribe",
  "groups": ["premium"],
  "feeds": ["football"],
  "eventTypes": ["goal.scored"]
}
```

Target:

```json
{
  "type": "subscribe",
  "subscriptions": [
    "group:premium",
    "feed:football",
    "event:goal.scored"
  ]
}
```

Unsubscribe:

```json
{
  "type": "unsubscribe",
  "subscriptions": [
    "feed:football"
  ]
}
```

Do not retain redundant category fields solely for convenience if V1 compatibility is not frozen.

# Authorization Simplification

Subscription authorization should operate on generic subscription keys.

Conceptually:

```text
Can this connection subscribe to "tenant:abc:group:premium"?
```

Do not keep separate category-specific authorization branches unless the implementation truly needs technically distinct behavior.

Reuse the current authorization boundary if it can be simplified.

Do not introduce another abstraction unless necessary.

# Routing Simplification

The routing path should conceptually become:

```text
Notification
    ├── UserIds
    └── Subscriptions
```

Routing should perform:

```text
Route direct users
Route generic subscriptions
```

Preserve existing behavior for:
- multiple direct users
- multiple subscriptions
- multiple active connections per user
- notification matching through more than one route
- expiration
- bounded buffers
- slow-client policy
- send ordering
- cancellation
- failure cleanup

# Registry Simplification

If the implementation currently keeps separate structures such as:

```text
group -> connections
feed -> connections
event -> connections
```

replace them with one generic index where appropriate:

```text
subscriptionKey -> connections
```

Keep the implementation direct and understandable.

Do not introduce:
- generic subscription strategy factories
- category handler hierarchies
- polymorphic subscription types
- type registries
- reflection-based routing

# Validation

A routable notification should require at least one destination:

```text
UserIds
OR
Subscriptions
```

Handle null, empty, duplicate, whitespace/invalid keys, and duplicate user IDs according to existing project conventions.

# TDD Workflow

Use `Agents/03-tdd-test-agent.md` for cohesive slices.

Suggested sequence:

## Slice 1 — Notification contract
- UserIds-only notification
- Subscriptions-only notification
- both
- neither
- expiration still works

## Slice 2 — Subscription registry
- subscribe one key
- subscribe several keys
- duplicate subscription behavior
- unsubscribe one
- unsubscribe several
- cleanup on connection close

## Slice 3 — Authorization
- allowed generic subscription
- denied generic subscription
- denied subscription does not mutate state
- authorizer receives the generic key and relevant identity/context

## Slice 4 — Routing
- direct user routing
- generic subscription routing
- multiple subscriptions
- multiple connections matching a subscription
- direct user + subscription combination
- no matching recipient
- expired notification not routed
- ordering preserved

## Slice 5 — Protocol
- subscribe with `subscriptions`
- unsubscribe with `subscriptions`
- malformed payload
- missing subscriptions
- old category-specific fields according to clean-break behavior

## Slice 6 — Cleanup
- obsolete category-specific routing structures removed
- `NotificationKind` removed if unused
- no category-specific branch remains solely for legacy structure

For each slice:

```text
failing tests
    ↓
minimum coherent implementation
    ↓
focused tests pass
    ↓
affected suite passes
    ↓
refactor while green
```

# Compatibility Policy

This project is still preparing V1.

Prefer a clean break.

Unless a strong repository-specific reason exists, remove:

```text
NotificationKind
Groups
Feeds
EventTypes
```

Do NOT add deprecated aliases merely to preserve an unreleased API.

If samples already exist, update them only after the core refactor is green and stable.

# Simplicity Gate

This refactor is successful only if it reduces conceptual and implementation complexity.

Expected improvements:
- fewer public routing properties
- fewer routing branches
- fewer category-specific protocol models
- fewer registry/index structures
- fewer category-specific tests
- simpler authorization input
- less duplicate code
- no `NotificationKind` if it no longer drives behavior

Do not accept a refactor that merely renames the same complexity.

# Documentation Changes

After the core refactor is green, update relevant documentation.

At minimum review:
- `README.md`
- `docs/architecture.md`
- `docs/protocol.md`
- `docs/message-source.md`
- `docs/limitations.md`
- `docs/decision-log.md`
- any sample docs already present

Document clearly:

> The library treats subscription keys as opaque identifiers. Concepts such as groups, feeds, events, roles, tenants, partners, or channels are defined and authorized by the consuming application.

Also document:

> Direct UserIds remain separate because direct-recipient routing is bound to authenticated user identity rather than client-controlled subscriptions.

# Required Completion Report

When complete, report:

```text
Refactor Status:

Public API changes:
- ...

Types/properties removed:
- ...

Types/properties added:
- ...

Routing simplifications:
- ...

Registry simplifications:
- ...

Authorization changes:
- ...

Protocol changes:
- ...

Tests added/updated:
- ...

Production code deleted/simplified:
- ...

Compatibility concerns:
- ...

Multi-tenant/partner responsibility:
- ...

Full test result:
- ...

Development Rules review:
- ...

Remaining risks:
- ...
```

Do not declare success until the complete relevant test suite passes.

# Non-Goals

Do not use this refactor to implement:
- tenant support
- partner support
- distributed presence or targeted server routing
- Redis/backplane
- replay
- ACK tracking
- binary protocol
- new queue providers
- generic broker frameworks
- unrelated performance rewrites

Keep the change focused.
