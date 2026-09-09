# Notification Producer API Sample

This .NET 8 minimal Web API publishes neutral notification JSON through KafkaHighThroughput. It never calls the WebSocket library directly.

## Run

Start Kafka using the root `compose.yaml`, then run the API from the repository root:

```powershell
dotnet run --project samples/NotificationProducer --urls http://localhost:5001
```

The health endpoint is `GET /`. Notification endpoints are:

Swagger UI is available at `http://localhost:5001/swagger`, with the OpenAPI document at `http://localhost:5001/swagger/v1/swagger.json`. The UI can execute each notification endpoint directly.

| Endpoint | Body routing fields |
|---|---|
| `POST /api/notifications/users` | `users` contains one or more user IDs |
| `POST /api/notifications/subscriptions` | `subscriptions` contains one or more opaque subscription keys |
| `POST /api/notifications` | Any combination of `userIds` and `subscriptions` |

Targeted request:

```json
{
  "users": ["user-1", "user-2"],
  "payload": { "score": 7 },
  "expiresAt": "2026-09-06T12:30:00Z",
  "key": "user:user-1"
}
```

Combined request:

```json
{
  "userIds": ["user-1"],
  "subscriptions": [
    "group:operators",
    "feed:match-42",
    "tenant:abc:event:score.changed"
  ],
  "payload": { "score": 7 },
  "key": "order:42"
}
```

PowerShell example:

```powershell
$body = @{ users = @('user-1'); payload = @{ score = 7 } } | ConvertTo-Json -Depth 4
Invoke-RestMethod -Method Post -Uri http://localhost:5001/api/notifications/users -ContentType application/json -Body $body
```

A successful Kafka delivery returns HTTP 202 with the message ID, effective key, topic, partition, and offset. Missing payloads, empty/blank users or subscriptions, non-UTC expiry values, and blank explicit keys return HTTP 400 validation problems.

The sample API has no authentication and is intended for local development only. Add application authentication and authorization before exposing an equivalent publishing endpoint outside a trusted development environment.

Subscription values are opaque to the library and samples. The consuming application defines and authorizes conventions such as `role:admin`, `tenant:abc:group:premium`, or `partner:p1:event:deposit.completed`. Direct user IDs remain separate because they correspond to authenticated identities rather than client-controlled subscriptions.

## Kafka key and ordering

Use `key` to choose an application ordering domain, such as `user:user-1`, `feed:match-42`, or `order:12345`. When omitted, the API derives a key from the first user or uses the first subscription key.

Keys define Kafka partition affinity; they do not provide a global order across unrelated keys or partitions. Never use a WebSocket server identity as the key.

## Configuration

- `NotificationProducer.ProducerName` selects the named KafkaHighThroughput producer.
- `NotificationProducer.Topic` selects the destination topic.
- `KafkaProducerClients.Producers` configures brokers, acknowledgements, idempotence, compression, buffering, and metrics.

Both appsettings files are copied to build and publish output so the executable works from its output directory. Override broker values through environment variables or secret providers; do not commit credentials.

The HTTP request waits for Kafka's delivery report before returning. Provider retry/failure behavior remains KafkaHighThroughput configuration rather than WebSocket-library behavior. See `NotificationProducer.http` for executable request examples.
