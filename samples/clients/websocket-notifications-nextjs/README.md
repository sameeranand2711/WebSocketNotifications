# Next.js WebSocket Notification Client

This Next.js 16 sample demonstrates the V1 browser protocol with a reusable client and React hook.

## Features

- Connection state display
- Opaque subscription-key subscribe/unsubscribe
- Notification and protocol logs
- Heartbeat ping/pong handling
- Bounded exponential reconnect delay with jitter
- A single reconnect loop
- Automatic resubscription after reconnect
- Application-specific client messages
- Defensive server-message parsing

Notifications published while disconnected are not replayed.

## Configure and run

```powershell
Copy-Item .env.example .env.local
npm install
npm run dev
```

Open `http://localhost:3000`. The default environment value connects to:

```text
ws://localhost:5000/ws/notifications
```

Set `NEXT_PUBLIC_WEBSOCKET_URL` in `.env.local` for another host. Do not put credentials or long-lived secrets in a `NEXT_PUBLIC_` variable.

The UI appends `userId` only for the repository's local query-authentication sample. A production deployment should use a secure authenticated cookie/session or inject a URL/credential acquisition adapter appropriate to its host application.

## Reusable API

`src/lib/websocket/client.js` exports `WebSocketNotificationClient`:

```javascript
const client = new WebSocketNotificationClient({
  url,
  onState: console.log,
  onNotification: console.log,
  onError: console.error,
});

client.connect();
client.subscribe("group:operators");
client.unsubscribe("group:operators");
client.sendApplicationMessage({ type: "sample.client-event" });
client.disconnect();
```

The React UI uses `useWebSocketNotifications` in `src/hooks` to expose connection state, subscriptions, notifications, logs, and actions.

The client does not parse subscription prefixes. Values such as `group:operators`, `role:admin`, or `tenant:abc:market:nse` are application-defined opaque strings.

## Validate

```powershell
npm test
npm run typecheck
npm run build
```

The repository-level `scripts/run-live-e2e.ps1` imports the same reusable client code to validate a real Kafka-to-WebSocket notification.
