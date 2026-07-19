import * as signalR from '@microsoft/signalr';
import { getToken } from '@/lib/auth/tokenStorage';

const API_BASE_URL =
  import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:8888';

function buildFarmHubConnection(): signalR.HubConnection {
  return new signalR.HubConnectionBuilder()
    .withUrl(`${API_BASE_URL}/hubs/farm`, {
      accessTokenFactory: () => getToken() ?? '',
      // Auth is via the JWT bearer token above, not cookies — disabling credentialed
      // requests avoids requiring Access-Control-Allow-Credentials on the CORS policy.
      withCredentials: false,
    })
    .withAutomaticReconnect()
    .build();
}

export interface ConnectFarmHubOptions {
  /** Fires once, after the initial connection succeeds — not on every reconnect (see onReconnected for that). */
  onConnected?: () => void;
  onReconnecting?: (error?: Error) => void;
  onReconnected?: () => void;
  onClose?: (error?: Error) => void;
}

/**
 * Builds, wires standard lifecycle logging, and starts a shared-shape /hubs/farm connection —
 * extracted out of useFarmHub so a second real consumer (features/parts/hooks/usePartsHub) doesn't
 * duplicate the connection-building/lifecycle boilerplate. Callers register their own event
 * handlers on the returned `connection` (before or after this returns — SignalR queues `.on()`
 * registrations either way, they just won't retroactively catch anything already dispatched).
 */
export function connectFarmHub(options: ConnectFarmHubOptions = {}) {
  const connection = buildFarmHubConnection();

  connection.onreconnecting((error) => {
    console.warn('[farmHub] SignalR connection lost, reconnecting…', error);
    options.onReconnecting?.(error);
  });

  connection.onreconnected(() => {
    console.info('[farmHub] SignalR reconnected.');
    options.onReconnected?.();
  });

  connection.onclose((error) => {
    console.warn(
      '[farmHub] SignalR connection closed — no more live updates until this component remounts.',
      error,
    );
    options.onClose?.(error);
  });

  const startPromise = connection
    .start()
    .then(() => {
      console.info('[farmHub] SignalR connected.');
      options.onConnected?.();
    })
    .catch((error: unknown) => {
      // withAutomaticReconnect() handles retries; a failed initial connect just means no
      // live updates until connectivity returns.
      console.warn('[farmHub] SignalR failed to connect.', error);
    });

  return {
    connection,
    /**
     * Safe to call even while start() is still negotiating — calling connection.stop() mid-
     * negotiation throws ("connection was stopped during negotiation") and logs a console error,
     * which happens on every mount under React StrictMode's dev-only double-invoke. Waits for
     * start() to settle first so stop() always acts on a connection that's actually stoppable.
     */
    stop: () => startPromise.finally(() => connection.stop()),
  };
}
