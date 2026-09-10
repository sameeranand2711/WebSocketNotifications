# Documentation and Release Agent


# Prerequisite — Final Stage Only

Do not run this agent during active core-library implementation.

Run it after:
- core library is stabilized
- all final sample applications exist
- end-to-end validation succeeds
- the final quality score reaches the release benchmark and mandatory gates pass

Documentation should describe the finalized V1 library and samples, not an evolving intermediate design.

# Role

You create and verify the documentation and release artifacts for the WebSocket Notification V1 release.

Documentation must describe the implementation that actually exists.

Do not document speculative features as though they are implemented.

# Trigger

Run this agent near the end of development after the implementation and end-to-end flow are stable, and update documents again if later cycles materially change behavior.

# Required Artifacts

Create or update the following where appropriate.

## Root README.md

README must contain:

1. Project overview
2. V1 capabilities
3. V1 non-goals/limitations
4. High-level architecture
5. Quick start
6. Installation/package references
7. ASP.NET Core registration/configuration
8. Authentication/user-resolution model
9. Subscription model
10. Message-source integration
11. Notification contract example
12. WebSocket client protocol examples
13. Configuration example
14. Slow-client behavior
15. Heartbeat behavior
16. Delivery semantics
17. Ordering semantics
18. Running the sample host
19. Running the producer
20. Running the Next.js client
21. Running tests
22. Development/repository structure
23. Future roadmap

Keep the README practical. Link to deeper documents rather than making it enormous.

## docs/architecture.md

Describe:

```text
Message Source
    ↓
Notification Processing
    ↓
Recipient Routing
    ↓
Connection Buffer
    ↓
Single Send Loop
    ↓
WebSocket Client
```

Include:
- responsibilities
- component boundaries
- connection lifecycle
- subscription lifecycle
- inbound and outbound flows
- why provider-specific queue logic is outside the core
- V1 multi-server source-level fan-out
- process-local routing and the absence of distributed presence/targeted server resolution
- provider-specific independent source subscriptions per active server

## docs/protocol.md

Document the actual V1 JSON WebSocket protocol.

Include:
- connect assumptions
- authentication boundary
- subscribe request
- unsubscribe request
- server confirmations
- error responses
- heartbeat/ping/pong messages if application-level messages are used
- notification message
- application-specific inbound-message behavior
- close/error semantics
- maximum incoming message behavior

Include example JSON.

## docs/configuration.md

Document:
- all public options
- defaults
- valid ranges
- internal hard limits
- endpoint configuration
- heartbeat
- compression
- incoming/outgoing sizes
- outgoing buffer
- slow-client policy
- invalid configuration behavior

The docs must match code defaults exactly.

## docs/message-source.md

Explain the provider-neutral message-source abstraction.

Include:
- responsibility of the WebSocket library
- responsibility of provider adapter
- success/failure callback semantics
- cancellation
- source deserialization responsibility
- KafkaHighThroughput sample adapter
- how RabbitMQ/ZeroMQ could implement the same boundary without modifying core

Do not turn this into a universal broker framework.

## docs/delivery-semantics.md

State clearly:

```text
V1 successful delivery =
successful WebSocket send operation.
```

The library does not guarantee:
- client application processing
- client acknowledgement
- replay
- offline delivery
- exactly-once delivery

Explain:
- no WebSocket retry
- application-managed acknowledgement possibility
- at-least-once duplicates may exist upstream

## docs/ordering.md

Document:
- source ordering is preserved where provided
- no global ordering
- single sender per connection
- Kafka key can define ordering domain in Kafka-backed application
- application chooses Kafka key
- WebSocket library does not generate Kafka keys
- different partitions/keys do not have a defined relative global order

## docs/testing.md

Document:
- test projects
- how to run tests
- test categories
- integration tests
- end-to-end validation
- load/performance testing commands if available
- current test/coverage summary if the project generates it

## docs/development.md

Document:
- TDD workflow
- simplicity rules
- dependency rules
- coding standards
- contribution workflow
- how agents are intended to be used
- development cycle
- scoring gate

Link or summarize `02-development-rules-agent.md`.

## docs/limitations.md

Explicitly state V1 limitations:
- no distributed presence or targeted recipient-to-server resolution
- no Redis/backplane
- no replay
- no durable offline notification store
- no built-in ACK tracking
- no built-in WebSocket retry
- no exact-once guarantee
- JSON text protocol only
- tenant/scope deferred
- binary protocol deferred
- subscription TTL deferred
- multi-region deferred

## CHANGELOG.md

Create V1 changelog with:
- Added
- Behavior
- Known limitations

Use a standard readable changelog format.

## RELEASE_NOTES.md

Create concise V1 release notes:
- what the release provides
- how to try it
- major limitations
- compatibility/runtime requirements
- scoring result and release status

## docs/decision-log.md

Record important architectural decisions concisely.

Examples:
- provider-neutral message-source abstraction
- no direct Kafka dependency
- application-provided user resolver
- application-controlled subscription authorization
- multi-server source-level fan-out with local in-memory routing
- bounded per-connection buffers
- no WebSocket retries
- application-owned ACK semantics
- JSON protocol
- Kafka key belongs to producer/domain ordering

Do not record every trivial implementation detail.

# Diagrams

Use simple Mermaid or text diagrams that render in Markdown.

Do not require external proprietary diagram tooling.

# Documentation Quality Rules

- use examples copied from actual public API where practical
- run code snippets mentally/with compilation where possible
- do not invent class/method names that do not exist
- do not document future plans as implemented
- keep terminology consistent across documents
- make configuration names/defaults exact
- link documents from README
- avoid duplicate walls of text


## Sample configuration files

Verify the .NET sample applications contain:
- `appsettings.json`
- `appsettings.Development.json` where appropriate

The README/configuration docs must show the actual configuration hierarchy used by the samples.

Clearly distinguish:
- WebSocket library settings
- provider-specific queue settings
- secret values supplied through environment variables/user-secrets

Document both:
- `IConfiguration`/section binding
- programmatic options configuration

Do not imply that the library requires `appsettings.json`; explain that the samples use it while the library relies on standard .NET configuration/options abstractions.

# Final Verification

Before accepting documentation:

1. Compare README setup instructions with the sample.
2. Compare configuration docs with options code.
3. Compare protocol docs with protocol parser/models.
4. Compare message-source docs with actual interface.
5. Compare limitations with V1 implementation.
6. Verify all referenced file paths/projects exist.
7. Run documented commands where practical.
8. Fix stale docs.

# Required Output

Report:
- documents created/updated
- implementation inconsistencies discovered
- commands verified
- documentation gaps remaining
