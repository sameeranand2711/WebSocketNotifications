// @ts-check

import { parseServerMessage, subscriptionKey } from "./protocol.js";

const OPEN = 1;
const reservedApplicationTypes = new Set([
  "subscribe",
  "unsubscribe",
  "ping",
  "pong",
  "notification",
  "error",
]);

/**
 * Small reusable client that owns reconnect, heartbeat response, and subscription replay.
 * The client intentionally provides no delivery acknowledgement or missed-message replay.
 */
export class WebSocketNotificationClient {
  /**
   * @param {{
   *   url: string,
   *   createSocket?: (url: string) => WebSocket,
   *   baseReconnectDelayMs?: number,
   *   maxReconnectDelayMs?: number,
   *   random?: () => number,
   *   schedule?: (callback: () => void, delay: number) => unknown,
   *   cancelSchedule?: (handle: unknown) => void,
   *   onState?: (state: string) => void,
   *   onNotification?: (message: Record<string, unknown>) => void,
   *   onProtocolMessage?: (message: Record<string, unknown>) => void,
   *   onError?: (error: Error) => void
   * }} options
   */
  constructor(options) {
    this.url = options.url;
    this.createSocket = options.createSocket ?? ((url) => new WebSocket(url));
    this.baseReconnectDelayMs = options.baseReconnectDelayMs ?? 500;
    this.maxReconnectDelayMs = options.maxReconnectDelayMs ?? 15_000;
    this.random = options.random ?? Math.random;
    this.schedule = options.schedule ?? ((callback, delay) => setTimeout(callback, delay));
    this.cancelSchedule = options.cancelSchedule ?? ((handle) => clearTimeout(/** @type {number} */ (handle)));
    this.onState = options.onState ?? (() => {});
    this.onNotification = options.onNotification ?? (() => {});
    this.onProtocolMessage = options.onProtocolMessage ?? (() => {});
    this.onError = options.onError ?? (() => {});
    /** @type {Map<string, { kind: string, value: string }>} */
    this.subscriptions = new Map();
    /** @type {WebSocket | null} */
    this.socket = null;
    /** @type {unknown | null} */
    this.reconnectHandle = null;
    this.reconnectAttempt = 0;
    this.shouldReconnect = false;
  }

  connect() {
    this.shouldReconnect = true;
    if (this.socket && (this.socket.readyState === 0 || this.socket.readyState === OPEN)) return;
    this.openSocket();
  }

  disconnect() {
    this.shouldReconnect = false;
    if (this.reconnectHandle !== null) {
      this.cancelSchedule(this.reconnectHandle);
      this.reconnectHandle = null;
    }
    this.socket?.close(1000, "Client disconnect");
    this.socket = null;
    this.onState("disconnected");
  }

  /** @param {"group" | "feed" | "eventType"} kind @param {string} value */
  subscribe(kind, value) {
    const subscription = { kind, value };
    this.subscriptions.set(subscriptionKey(subscription), subscription);
    this.send({ type: "subscribe", requestId: crypto.randomUUID(), ...subscription });
  }

  /** @param {"group" | "feed" | "eventType"} kind @param {string} value */
  unsubscribe(kind, value) {
    const subscription = { kind, value };
    this.subscriptions.delete(subscriptionKey(subscription));
    this.send({ type: "unsubscribe", requestId: crypto.randomUUID(), ...subscription });
  }

  /** @param {{ type: string, [key: string]: unknown }} message */
  sendApplicationMessage(message) {
    if (reservedApplicationTypes.has(message.type)) {
      throw new Error("Application message type is reserved by the notification protocol.");
    }
    this.send(message);
  }

  getSubscriptions() {
    // Return a snapshot so callers cannot mutate the replay set without using the API.
    return [...this.subscriptions.values()];
  }

  openSocket() {
    this.onState(this.reconnectAttempt === 0 ? "connecting" : "reconnecting");
    const socket = this.createSocket(this.url);
    this.socket = socket;
    socket.onopen = () => {
      this.reconnectAttempt = 0;
      this.reconnectHandle = null;
      this.onState("connected");
      // Subscriptions live only in host memory, so every new transport must recreate them.
      for (const subscription of this.subscriptions.values()) {
        this.send({ type: "subscribe", requestId: crypto.randomUUID(), ...subscription });
      }
    };
    socket.onmessage = (event) => this.handleMessage(String(event.data));
    socket.onerror = () => this.onError(new Error("WebSocket transport error."));
    socket.onclose = () => {
      if (this.socket === socket) this.socket = null;
      if (this.shouldReconnect) this.scheduleReconnect();
      else this.onState("disconnected");
    };
  }

  scheduleReconnect() {
    if (this.reconnectHandle !== null) return;
    const exponential = Math.min(
      this.maxReconnectDelayMs,
      this.baseReconnectDelayMs * 2 ** this.reconnectAttempt++,
    );
    // Jitter spreads reconnect attempts when many clients lose the same server at once.
    const delay = exponential * (0.5 + this.random() * 0.5);
    this.onState("reconnecting");
    this.reconnectHandle = this.schedule(() => {
      this.reconnectHandle = null;
      if (this.shouldReconnect) this.openSocket();
    }, delay);
  }

  /** @param {string} text */
  handleMessage(text) {
    try {
      const message = parseServerMessage(text);
      if (message.type === "ping" && typeof message.nonce === "string") {
        this.send({ type: "pong", nonce: message.nonce });
      } else if (message.type === "notification") {
        this.onNotification(message);
      } else {
        this.onProtocolMessage(message);
      }
    } catch (error) {
      this.onError(error instanceof Error ? error : new Error(String(error)));
    }
  }

  /** @param {Record<string, unknown>} message */
  send(message) {
    if (this.socket?.readyState === OPEN) this.socket.send(JSON.stringify(message));
  }
}
