// @ts-check

const serverTypes = new Set(["notification", "error", "subscribed", "unsubscribed", "ping"]);

/**
 * @param {string} text
 * @returns {{ type: string, [key: string]: unknown }}
 */
export function parseServerMessage(text) {
  let value;
  try {
    value = JSON.parse(text);
  } catch {
    throw new Error("Server message was not valid JSON.");
  }

  if (value === null || typeof value !== "object" || Array.isArray(value)) {
    throw new Error("Server message must be a JSON object.");
  }

  if (typeof value.type !== "string" || !serverTypes.has(value.type)) {
    throw new Error("Server message has an unknown type.");
  }

  return value;
}
