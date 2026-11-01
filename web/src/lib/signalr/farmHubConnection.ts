import * as signalR from '@microsoft/signalr';
import { useQuery } from '@tanstack/react-query';
import { createContext, useContext, useEffect, useRef } from 'react';
import { apiUrl } from '@/lib/api/apiUrl';
import { validToken } from '@/lib/auth/session';

export const farmConnectionQueryKey = ['farm-connection-status'] as const;
export type FarmConnectionStatus =
  | 'connecting'
  | 'connected'
  | 'reconnecting'
  | 'disconnected';

/** The tab's one /hubs/farm connection, opened by useFarmHub in AppShell; null until it exists. */
export const FarmHubContext = createContext<signalR.HubConnection | null>(null);

/** Runs `handler` for each `event` pushed on the tab's shared connection, for as long as the caller is mounted. */
export function useFarmHubEvent<Args extends unknown[]>(
  event: string,
  handler: (...args: Args) => void,
) {
  const connection = useContext(FarmHubContext);
  const latest = useRef(handler);
  latest.current = handler;
  useEffect(() => {
    if (!connection) return;
    const listener = (...args: Args) => latest.current(...args);
    connection.on(event, listener);
    return () => connection.off(event, listener);
  }, [connection, event]);
}

/**
 * Runs `callback` each time the shared connection comes back (or first connects while mounted): pushes sent while it
 * was down are never replayed, so this is the caller's catch-up.
 */
export function useOnFarmHubReconnected(callback: () => void) {
  const { data: status } = useQuery<FarmConnectionStatus | null>({
    queryKey: farmConnectionQueryKey,
    queryFn: () => null,
    enabled: false,
    staleTime: Number.POSITIVE_INFINITY,
  });
  const latest = useRef(callback);
  latest.current = callback;
  const previous = useRef(status);
  useEffect(() => {
    const before = previous.current;
    previous.current = status;
    if (status === 'connected' && before != null && before !== 'connected')
      latest.current();
  }, [status]);
}

function buildFarmHubConnection(): signalR.HubConnection {
  const connection = new signalR.HubConnectionBuilder()
    .withUrl(new URL(apiUrl('/hubs/farm'), window.location.origin).href, {
      accessTokenFactory: async () => (await validToken()) ?? '',
      withCredentials: true,
      // WebSockets only: a silent fallback to SSE/long polling would turn each push into its own HTTP round trip
      // through the tunnel.
      transport: signalR.HttpTransportType.WebSockets,
      skipNegotiation: true,
    })
    .withAutomaticReconnect({
      nextRetryDelayInMilliseconds: ({ previousRetryCount }) =>
        [0, 2000, 10000, 30000][Math.min(previousRetryCount, 3)],
    })
    .build();
  return connection;
}

export interface ConnectFarmHubOptions {
  /** Fires once, after the initial connection succeeds — not on every reconnect (see onReconnected for that). */
  onConnected?: () => void;
  onReconnecting?: (error?: Error) => void;
  onReconnected?: () => void;
  onClose?: (error?: Error) => void;
}

/**
 * Builds, wires standard lifecycle logging, and starts the /hubs/farm connection. Only useFarmHub calls this — one
 * connection per tab, shared with every other feature through FarmHubContext and useFarmHubEvent.
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
      const stopSession = () => {
        stopped = true;
        clearTimeout(retryTimer);
        void startPromise.finally(() => connection.stop());
      };
      window.addEventListener('openfarm:logout', stopSession, { once: true });
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

  const stopSession = () => {
    stopped = true;
    clearTimeout(retryTimer);
    void startPromise.finally(() => connection.stop());
  };
  window.addEventListener('openfarm:logout', stopSession, { once: true });
  startPromise = start();

  return {
    connection,
    // Wait for negotiation to settle before stopping, including StrictMode cleanup.
    stop: () => {
      window.removeEventListener('openfarm:logout', stopSession);
      stopped = true;
      clearTimeout(retryTimer);
      return startPromise.finally(() => connection.stop());
    },
  };
}
