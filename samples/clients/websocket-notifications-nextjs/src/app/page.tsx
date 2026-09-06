"use client";

import { FormEvent, useMemo, useState } from "react";
import { SubscriptionKind, useWebSocketNotifications } from "../hooks/useWebSocketNotifications";

const baseUrl = process.env.NEXT_PUBLIC_WEBSOCKET_URL ?? "ws://localhost:5000/ws/notifications";

const subscriptionLabels: Record<SubscriptionKind, string> = {
  group: "Group",
  feed: "Feed",
  eventType: "Event type",
};

const stateLabels: Record<string, string> = {
  connected: "Live",
  connecting: "Connecting",
  reconnecting: "Reconnecting",
  disconnected: "Offline",
};

function SignalMark() {
  return (
    <svg viewBox="0 0 48 48" role="img" aria-label="Signal Desk logo">
      <path d="M9 31.5V39h7.5M9 39l10.5-10.5 7 7L40 22" />
      <path d="M31 22h9v9" />
      <circle cx="14" cy="14" r="5" />
    </svg>
  );
}

function PlusIcon() {
  return <svg viewBox="0 0 20 20" aria-hidden="true"><path d="M10 4v12M4 10h12" /></svg>;
}

function CloseIcon() {
  return <svg viewBox="0 0 20 20" aria-hidden="true"><path d="m5 5 10 10M15 5 5 15" /></svg>;
}

function TrashIcon() {
  return <svg viewBox="0 0 20 20" aria-hidden="true"><path d="M4 6h12M8 3h4l1 3H7l1-3Zm-2 3 1 11h6l1-11M9 9v5m2-5v5" /></svg>;
}

function SendIcon() {
  return <svg viewBox="0 0 20 20" aria-hidden="true"><path d="m3 4 14 6-14 6 2-6-2-6Zm2 6h8" /></svg>;
}

function displayTime(notification: Record<string, unknown>) {
  const raw = notification.occurredAt ?? notification.sentAt ?? notification.timestamp;
  if (typeof raw !== "string") return "Just now";

  const date = new Date(raw);
  return Number.isNaN(date.getTime())
    ? "Just now"
    : date.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit" });
}

function notificationTitle(notification: Record<string, unknown>) {
  const value = notification.eventType ?? notification.type;
  return typeof value === "string" ? value.replace(/[._-]+/g, " ") : "Incoming notification";
}

export default function Home() {
  const [userId, setUserId] = useState("user-1");
  const [kind, setKind] = useState<SubscriptionKind>("group");
  const [value, setValue] = useState("operators");
  const url = useMemo(() => {
    const parsed = new URL(baseUrl);
    parsed.searchParams.set("userId", userId);
    return parsed.toString();
  }, [userId]);
  const notifications = useWebSocketNotifications(url);
  const stateLabel = stateLabels[notifications.state] ?? notifications.state;

  function subscribe(event: FormEvent) {
    event.preventDefault();
    if (value.trim()) notifications.subscribe(kind, value.trim());
  }

  return (
    <main className="app-shell">
      <header className="topbar">
        <div className="container-fluid app-container d-flex align-items-center justify-content-between gap-3">
          <a className="brand d-flex align-items-center gap-3 text-decoration-none" href="#top" aria-label="Signal Desk home">
            <span className="brand-mark"><SignalMark /></span>
            <span>
              <strong>Signal Desk</strong>
              <small>WebSocket command center</small>
            </span>
          </a>
          <div className={`connection-pill ${notifications.state}`} role="status" aria-live="polite">
            <span className="status-beacon" />
            <span>{stateLabel}</span>
          </div>
        </div>
      </header>

      <div id="top" className="container-fluid app-container page-content">
        <section className="hero-panel row g-0 align-items-stretch overflow-hidden">
          <div className="col-lg-8 hero-copy">
            <div className="eyebrow"><span>01</span> Real-time observatory</div>
            <h1>Every message,<br /><em>in sight.</em></h1>
            <p>Subscribe to the streams that matter and watch your application&apos;s signals arrive in real time.</p>
          </div>
          <div className="col-lg-4 hero-stats">
            <div className="metric">
              <span>Received</span>
              <strong>{String(notifications.notifications.length).padStart(2, "0")}</strong>
              <small>This session</small>
            </div>
            <div className="metric">
              <span>Channels</span>
              <strong>{String(notifications.subscriptions.length).padStart(2, "0")}</strong>
              <small>Actively tracked</small>
            </div>
          </div>
        </section>

        <div className="row g-4 workspace-row">
          <aside className="col-xl-4">
            <div className="control-stack">
              <section className="panel identity-panel">
                <div className="section-heading">
                  <span className="section-index">A</span>
                  <div>
                    <p>Connection identity</p>
                    <h2>Who&apos;s listening?</h2>
                  </div>
                </div>
                <label className="form-label" htmlFor="userId">Local sample user</label>
                <div className="input-group identity-input">
                  <span className="input-group-text" aria-hidden="true">@</span>
                  <input id="userId" className="form-control" value={userId} onChange={(event) => setUserId(event.target.value)} autoComplete="off" />
                </div>
                <p className="helper-text">Changing this value reconnects with a new sample identity. Production clients should use an authenticated session.</p>
              </section>

              <section className="panel subscription-panel">
                <div className="section-heading">
                  <span className="section-index">B</span>
                  <div>
                    <p>Stream control</p>
                    <h2>Tune your signal</h2>
                  </div>
                </div>
                <form onSubmit={subscribe}>
                  <div className="row g-2">
                    <div className="col-sm-5 col-xl-12 col-xxl-5">
                      <label className="form-label" htmlFor="subscriptionKind">Channel type</label>
                      <select id="subscriptionKind" className="form-select" value={kind} onChange={(event) => setKind(event.target.value as SubscriptionKind)}>
                        <option value="group">Group</option>
                        <option value="feed">Feed</option>
                        <option value="eventType">Event type</option>
                      </select>
                    </div>
                    <div className="col-sm-7 col-xl-12 col-xxl-7">
                      <label className="form-label" htmlFor="subscriptionValue">Channel value</label>
                      <input id="subscriptionValue" className="form-control" value={value} onChange={(event) => setValue(event.target.value)} placeholder="e.g. operators" autoComplete="off" />
                    </div>
                  </div>
                  <button className="btn btn-signal w-100" type="submit" disabled={!value.trim()}><PlusIcon /> Add subscription</button>
                </form>

                <div className="active-label d-flex justify-content-between align-items-center">
                  <span>Active channels</span>
                  <span className="count-badge">{notifications.subscriptions.length}</span>
                </div>
                <div className="subscription-list">
                  {notifications.subscriptions.length === 0 ? (
                    <p className="empty-channels">No channels selected yet.</p>
                  ) : notifications.subscriptions.map((subscription) => (
                    <div className="subscription-chip" key={`${subscription.kind}:${subscription.value}`}>
                      <span><small>{subscriptionLabels[subscription.kind]}</small><strong>{subscription.value}</strong></span>
                      <button type="button" onClick={() => notifications.unsubscribe(subscription.kind, subscription.value)} aria-label={`Unsubscribe from ${subscription.value}`}><CloseIcon /></button>
                    </div>
                  ))}
                </div>
              </section>
            </div>
          </aside>

          <div className="col-xl-8">
            <section className="panel inbox-panel">
              <div className="inbox-header d-flex flex-wrap align-items-center justify-content-between gap-3">
                <div className="section-heading mb-0">
                  <span className="section-index">C</span>
                  <div><p>Live transmission</p><h2>Notification inbox</h2></div>
                </div>
                <button type="button" className="btn btn-clean" onClick={notifications.clearNotifications} disabled={notifications.notifications.length === 0}><TrashIcon /> Clear all</button>
              </div>

              <div className="notification-feed" aria-live="polite">
                {notifications.notifications.length === 0 ? (
                  <div className="empty-state">
                    <div className="radar" aria-hidden="true"><span /><i /></div>
                    <h3>Scanning for signals</h3>
                    <p>Your incoming notifications will appear here as soon as a subscribed event lands.</p>
                  </div>
                ) : notifications.notifications.map((notification, index) => (
                  <article className="notification-card" key={`${String(notification.messageId ?? "notification")}-${index}`}>
                    <div className="notification-rail"><span>{String(notifications.notifications.length - index).padStart(2, "0")}</span></div>
                    <div className="notification-body">
                      <div className="d-flex flex-wrap align-items-start justify-content-between gap-2">
                        <div><span className="event-kicker">Incoming event</span><h3>{notificationTitle(notification)}</h3></div>
                        <time>{displayTime(notification)}</time>
                      </div>
                      <pre>{JSON.stringify(notification, null, 2)}</pre>
                    </div>
                  </article>
                ))}
              </div>
            </section>
          </div>
        </div>

        <div className="row g-4 utility-row">
          <div className="col-lg-8">
            <section className="terminal-panel h-100">
              <div className="terminal-bar d-flex align-items-center justify-content-between gap-3">
                <span><i /><i /><i /></span><strong>connection.log</strong><small>{notifications.log.length} entries</small>
              </div>
              <div className="terminal-content">
                {notifications.log.length === 0
                  ? <span className="terminal-muted">Waiting for connection activity…</span>
                  : notifications.log.map((entry, index) => (
                    <div className="log-line" key={`${entry}-${index}`}><span>{String(notifications.log.length - index).padStart(2, "0")}</span><code>{entry}</code></div>
                  ))}
              </div>
            </section>
          </div>
          <div className="col-lg-4">
            <section className="outbound-panel h-100">
              <span className="outbound-number">02</span>
              <p>Test the return path</p>
              <h2>Send an application event.</h2>
              <button type="button" className="btn btn-dark-send" onClick={() => notifications.sendApplicationMessage("sample.client-event")} disabled={notifications.state !== "connected"}>Send sample event <SendIcon /></button>
              <small>{notifications.state === "connected" ? "Connection ready" : "Available when connected"}</small>
            </section>
          </div>
        </div>
      </div>

      <footer>
        <div className="container-fluid app-container d-flex flex-wrap justify-content-between gap-2">
          <span>Signal Desk / Client sample</span><span>WebSocket Notifications · V1</span>
        </div>
      </footer>
    </main>
  );
}
