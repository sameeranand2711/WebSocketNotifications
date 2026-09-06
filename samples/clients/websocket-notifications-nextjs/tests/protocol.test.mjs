import test from "node:test";
import assert from "node:assert/strict";
import { parseServerMessage, subscriptionKey } from "../src/lib/websocket/protocol.js";

test("parseServerMessage accepts known protocol messages", () => {
  assert.equal(parseServerMessage('{"type":"notification","messageId":"m1"}').messageId, "m1");
  assert.equal(subscriptionKey({ kind: "group", value: "operators" }), "group:operators");
});

test("parseServerMessage rejects malformed and unknown messages", () => {
  assert.throws(() => parseServerMessage("{"), /valid JSON/);
  assert.throws(() => parseServerMessage("[]"), /JSON object/);
  assert.throws(() => parseServerMessage('{"type":"future"}'), /unknown type/);
});
