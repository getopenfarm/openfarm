import * as signalR from '@microsoft/signalr';
import { apiUrl } from '@/lib/api/apiUrl';
import { getToken } from '@/lib/auth/tokenStorage';

function buildFarmHubConnection(): signalR.HubConnection {
  return new signalR.HubConnectionBuilder()
    .withUrl(new URL(apiUrl('/hubs/farm'), window.location.origin).href, {
      accessTokenFactory: () => getToken() ?? '',
      // Auth is via the JWT bearer token above, not cookies — disabling credentialed
      // requests avoids requiring Access-Control-Allow-Credentials on the CORS policy.
      withCredentials: false,
    })
    .withAutomaticReconnect({
      nextRetryDelayInMilliseconds: ({ previousRetryCount }) =>
        [0, 2000, 10000, 30000][Math.min(previousRetryCount, 3)],
    })
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

  let stopped = false;
  let hasConnected = false;
  let retryTimer: ReturnType<typeof setTimeout> | undefined;
  let startPromise: Promise<void> = Promise.resolve();

  const scheduleStart = () => {
    if (stopped || retryTimer !== undefined) return;
    retryTimer = setTimeout(() => {
      retryTimer = undefined;
      startPromise = start();
    }, 5000);
  };

  const start = async () => {
    try {
      await connection.start();
      if (stopped) return;
      console.info('[farmHub] SignalR connected.');
      if (hasConnected) options.onReconnected?.();
      else options.onConnected?.();
      hasConnected = true;
    } catch (error: unknown) {
      if (stopped) return;
      console.warn('[farmHub] SignalR failed to connect; retrying.', error);
      options.onClose?.(error instanceof Error ? error : undefined);
      scheduleStart();
    }
  };

  connection.onreconnecting((error) => {
    if (stopped) return;
    console.warn('[farmHub] SignalR connection lost, reconnecting…', error);
    options.onReconnecting?.(error);
  });

  connection.onreconnected(() => {
    if (stopped) return;
    console.info('[farmHub] SignalR reconnected.');
    options.onReconnected?.();
  });

  connection.onclose((error) => {
    if (stopped) return;
    console.warn('[farmHub] SignalR connection closed; retrying.', error);
    options.onClose?.(error);
    scheduleStart();
  });

  startPromise = start();

  return {
    connection,
    // Wait for negotiation to settle before stopping, including StrictMode cleanup.
    stop: () => {
      stopped = true;
      clearTimeout(retryTimer);
      return startPromise.finally(() => connection.stop());
    },
  };
}
