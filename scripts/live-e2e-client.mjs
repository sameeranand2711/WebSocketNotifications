import { WebSocketNotificationClient } from "../samples/clients/websocket-notifications-nextjs/src/lib/websocket/client.js";

const [url, expectedRunId] = process.argv.slice(2);
if (!url || !expectedRunId) {
  console.error("Usage: node scripts/live-e2e-client.mjs <websocket-url> <run-id>");
  process.exit(2);
}

const timeout = setTimeout(() => {
  console.error(`Timed out waiting for live notification ${expectedRunId}.`);
  client.disconnect();
  process.exit(1);
}, 30_000);

const client = new WebSocketNotificationClient({
  url,
  onState: (state) => console.log(`client:${state}`),
  onNotification: (message) => {
    if (message.payload?.text !== "Hello from Kafka") return;
    clearTimeout(timeout);
    console.log(JSON.stringify({ runId: expectedRunId, notification: message }));
    client.disconnect();
    process.exit(0);
  },
  onError: (error) => console.error(error.message),
});

client.connect();
