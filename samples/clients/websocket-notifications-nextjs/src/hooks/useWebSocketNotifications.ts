"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { WebSocketNotificationClient } from "../lib/websocket/client.js";

export type SubscriptionKind = "group" | "feed" | "eventType";
type Subscription = { kind: SubscriptionKind; value: string };
type ServerMessage = Record<string, unknown>;

/**
 * Binds the reusable transport client to React state and disposes it whenever the URL changes.
 */
export function useWebSocketNotifications(url: string) {
  const clientRef = useRef<WebSocketNotificationClient | null>(null);
  const [state, setState] = useState("disconnected");
  const [notifications, setNotifications] = useState<ServerMessage[]>([]);
  const [subscriptions, setSubscriptions] = useState<Subscription[]>([]);
  const [log, setLog] = useState<string[]>([]);

  useEffect(() => {
    const client = new WebSocketNotificationClient({
      url,
      onState: (nextState) => {
        setState(nextState);
        setLog((entries) => [`state: ${nextState}`, ...entries].slice(0, 50));
      },
      onNotification: (message) => setNotifications((items) => [message, ...items]),
      onProtocolMessage: (message) =>
        setLog((entries) => [`protocol: ${JSON.stringify(message)}`, ...entries].slice(0, 50)),
      onError: (error) =>
        setLog((entries) => [`error: ${error.message}`, ...entries].slice(0, 50)),
    });
    clientRef.current = client;
    client.connect();
    // Closing during cleanup prevents an old identity/URL from reconnecting after rerender.
    return () => {
      client.disconnect();
      clientRef.current = null;
    };
  }, [url]);

  return useMemo(
    () => ({
      state,
      notifications,
      subscriptions,
      log,
      subscribe(kind: SubscriptionKind, value: string) {
        clientRef.current?.subscribe(kind, value);
        setSubscriptions(clientRef.current?.getSubscriptions() as Subscription[] ?? []);
      },
      unsubscribe(kind: SubscriptionKind, value: string) {
        clientRef.current?.unsubscribe(kind, value);
        setSubscriptions(clientRef.current?.getSubscriptions() as Subscription[] ?? []);
      },
      clearNotifications() {
        setNotifications([]);
      },
      sendApplicationMessage(type: string) {
        clientRef.current?.sendApplicationMessage({ type, sentAt: new Date().toISOString() });
      },
    }),
    [log, notifications, state, subscriptions],
  );
}
