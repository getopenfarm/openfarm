import { useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { connectFarmHub } from '@/lib/signalr/farmHubConnection';

interface ProductionRunStatusChangedPayload {
  productionRunId: string;
  status: string;
}

interface PrintJobStatusChangedPayload {
  printJobId: string;
  status: string;
}

interface PrintJobDispatchedPayload {
  printJobId: string;
  printerId: string;
}

/** Matches every production-run/print-job list and detail query cached so far. */
function invalidateProductionQueries(
  queryClient: ReturnType<typeof useQueryClient>,
) {
  queryClient.invalidateQueries({
    predicate: (query) =>
      typeof query.queryKey[0] === 'string' &&
      (query.queryKey[0].startsWith('/api/v1/production-runs') ||
        query.queryKey[0].startsWith('/api/v1/print-jobs')),
  });
}

/**
 * Opens the /hubs/farm SignalR connection and invalidates the affected production-run/print-job
 * queries on every push — no polling fallback, matching every other real-time feature in this app.
 * Status is always re-fetched (never optimistically written from the push payload alone) since a
 * live-integrated job's remote-control actions are deliberately request/confirm, not optimistic
 * (research.md R12, FR-024a) — the client always re-reads the server's own authoritative state.
 */
export function useProductionHub() {
  const queryClient = useQueryClient();

  useEffect(() => {
    const { connection, stop } = connectFarmHub({
      onConnected: () => invalidateProductionQueries(queryClient),
      onReconnected: () => invalidateProductionQueries(queryClient),
    });

    connection.on(
      'ProductionRunStatusChanged',
      (_payload: ProductionRunStatusChangedPayload) => {
        invalidateProductionQueries(queryClient);
      },
    );

    connection.on(
      'PrintJobStatusChanged',
      (_payload: PrintJobStatusChangedPayload) => {
        invalidateProductionQueries(queryClient);
      },
    );

    connection.on(
      'PrintJobDispatched',
      (_payload: PrintJobDispatchedPayload) => {
        invalidateProductionQueries(queryClient);
      },
    );

    return () => {
      stop();
    };
  }, [queryClient]);
}
