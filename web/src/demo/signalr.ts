// The demo build's stand-in for @microsoft/signalr (vite.config.ts aliases it in `--mode demo`). There is no
// server: each connection starts at once, and the demo simulator pushes server events through `emit`.
type Handler = (...args: never[]) => void;

const live = new Set<DemoHubConnection>();

/** Delivers a farm-hub event to every live connection, as the server would. */
export function emit(event: string, ...args: unknown[]) {
  for (const connection of live)
    for (const handler of connection.handlers.get(event) ?? [])
      (handler as (...values: unknown[]) => void)(...args);
}

class DemoHubConnection {
  readonly handlers = new Map<string, Set<Handler>>();
  on(event: string, handler: Handler) {
    const set = this.handlers.get(event) ?? new Set<Handler>();
    set.add(handler);
    this.handlers.set(event, set);
  }
  off(event: string, handler?: Handler) {
    if (handler) this.handlers.get(event)?.delete(handler);
    else this.handlers.delete(event);
  }
  start() {
    live.add(this);
    return Promise.resolve();
  }
  stop() {
    live.delete(this);
    return Promise.resolve();
  }
  onreconnecting() {}
  onreconnected() {}
  onclose() {}
}

export class HubConnectionBuilder {
  withUrl() {
    return this;
  }
  withAutomaticReconnect() {
    return this;
  }
  build() {
    return new DemoHubConnection();
  }
}

export const HttpTransportType = {
  None: 0,
  WebSockets: 1,
  ServerSentEvents: 2,
  LongPolling: 4,
} as const;

export type HubConnection = DemoHubConnection;
