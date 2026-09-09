import test from "node:test";
import assert from "node:assert/strict";
import { WebSocketNotificationClient } from "../src/lib/websocket/client.js";

class FakeSocket {
  readyState = 0;
  sent = [];
  onopen = null;
  onmessage = null;
  onerror = null;
  onclose = null;

  open() { this.readyState = 1; this.onopen?.({}); }
  send(message) { this.sent.push(message); }
  close() { this.readyState = 3; this.onclose?.({}); }
  failClose() { this.readyState = 3; this.onclose?.({}); }
  message(data) { this.onmessage?.({ data }); }
}

function harness() {
  const sockets = [];
  const scheduled = [];
  const client = new WebSocketNotificationClient({
    url: "ws://example.test/ws",
    createSocket: () => {
      const socket = new FakeSocket();
      sockets.push(socket);
      return socket;
    },
    random: () => 0,
    schedule: (callback) => { scheduled.push(callback); return callback; },
    cancelSchedule: (handle) => {
      const index = scheduled.indexOf(handle);
      if (index >= 0) scheduled.splice(index, 1);
    },
  });
  return { client, sockets, scheduled };
}

test("reconnect schedules one loop and resubscribes after open", () => {
  const { client, sockets, scheduled } = harness();
  client.connect();
  sockets[0].open();
  client.subscribe("tenant:abc:group:operators");
  sockets[0].failClose();
  sockets[0].failClose();
  assert.equal(scheduled.length, 1);

  scheduled.shift()();
  sockets[1].open();
  const commands = sockets[1].sent.map(JSON.parse);
  assert.deepEqual(commands.map((command) => [command.type, command.subscriptions]), [
    ["subscribe", ["tenant:abc:group:operators"]],
  ]);
});

test("unsubscribe while disconnected prevents resubscription", () => {
  const { client, sockets, scheduled } = harness();
  client.connect();
  sockets[0].open();
  client.subscribe("feed:match-42");
  sockets[0].failClose();
  client.unsubscribe("feed:match-42");
  scheduled.shift()();
  sockets[1].open();
  assert.deepEqual(sockets[1].sent, []);
});

test("intentional disconnect cancels reconnect", () => {
  const { client, sockets, scheduled } = harness();
  client.connect();
  sockets[0].open();
  sockets[0].failClose();
  client.disconnect();
  assert.equal(scheduled.length, 0);
});

test("ping receives matching pong and notification is surfaced", () => {
  const notifications = [];
  const socket = new FakeSocket();
  const client = new WebSocketNotificationClient({
    url: "ws://example.test/ws",
    createSocket: () => socket,
    onNotification: (message) => notifications.push(message),
  });
  client.connect();
  socket.open();
  socket.message('{"type":"ping","nonce":"n1"}');
  socket.message('{"type":"notification","messageId":"m1"}');

  assert.deepEqual(JSON.parse(socket.sent[0]), { type: "pong", nonce: "n1" });
  assert.equal(notifications[0].messageId, "m1");
});
