import { WebSocketNotificationClient } from "../samples/clients/websocket-notifications-nextjs/src/lib/websocket/client.js";

const [urlsValue, expectedMarker, expectedCountValue, forbiddenMarker] = process.argv.slice(2);
const urls = urlsValue?.split(",").filter(Boolean) ?? [];
const expectedCount = Number(expectedCountValue);

if (
  urls.length === 0 ||
  !expectedMarker ||
  !Number.isInteger(expectedCount) ||
  expectedCount < 1 ||
  expectedCount > urls.length
) {
  console.error(
    "Usage: node scripts/multi-host-e2e-client.mjs <comma-separated-urls> <expected-marker> <expected-count> [forbidden-marker]",
  );
  process.exit(2);
}

const connected = new Set();
const receiptCounts = new Map();
let completionHandle;
let finished = false;

const timeout = setTimeout(
  () => finish(1, `Timed out waiting for ${expectedCount} receipt(s) of ${expectedMarker}.`),
  90_000,
);

const clients = urls.map(
  (url, index) =>
    new WebSocketNotificationClient({
      url,
      onState: (state) => {
        if (state !== "connected") return;
        connected.add(index);
        if (connected.size === urls.length) {
          console.log(JSON.stringify({ type: "ready", clients: urls.length }));
        }
      },
      onNotification: (message) => {
        const marker = message.payload?.marker;
        if (forbiddenMarker && marker === forbiddenMarker) {
          finish(1, `Received forbidden offline marker ${forbiddenMarker} on client ${index}.`);
          return;
        }

        if (marker !== expectedMarker) return;
        const count = (receiptCounts.get(index) ?? 0) + 1;
        receiptCounts.set(index, count);
        if (count > 1) {
          finish(1, `Client ${index} received duplicate marker ${expectedMarker}.`);
          return;
        }

        if (receiptCounts.size === expectedCount && completionHandle === undefined) {
          completionHandle = setTimeout(() => {
            const totalReceipts = [...receiptCounts.values()].reduce((total, value) => total + value, 0);
            if (receiptCounts.size !== expectedCount || totalReceipts !== expectedCount) {
              finish(1, `Expected ${expectedCount} receipt(s), observed ${totalReceipts}.`);
              return;
            }

            console.log(
              JSON.stringify({
                type: "result",
                marker: expectedMarker,
                clients: urls.length,
                recipients: [...receiptCounts.keys()].sort(),
              }),
            );
            finish(0);
          }, 1_500);
        }
      },
      onError: (error) => console.error(`client-${index}: ${error.message}`),
    }),
);

for (const client of clients) client.connect();

function finish(exitCode, error) {
  if (finished) return;
  finished = true;
  clearTimeout(timeout);
  if (completionHandle !== undefined) clearTimeout(completionHandle);
  for (const client of clients) client.disconnect();
  if (error) console.error(error);
  setTimeout(() => process.exit(exitCode), 25);
}
