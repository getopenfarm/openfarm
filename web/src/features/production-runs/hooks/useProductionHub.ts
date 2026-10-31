import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef } from 'react';
import { connectFarmHub } from '@/lib/signalr/farmHubConnection';
import { type PrintJobSendProgress, sendProgressKey } from './sendProgress';

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
export function invalidateProductionQueries(
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
export function useProductionHub(
  onJobStatusChanged?: (jobId: string, status: string) => void,
) {
  const statusChangedRef = useRef(onJobStatusChanged);
  statusChangedRef.current = onJobStatusChanged;
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
      (payload: PrintJobStatusChangedPayload) => {
        statusChangedRef.current?.(payload.printJobId, payload.status);
        queryClient.resetQueries({
          queryKey: sendProgressKey(payload.printJobId),
        });
        invalidateProductionQueries(queryClient);
      },
    );

    connection.on('PartDeleted', () =>
      invalidateProductionQueries(queryClient),
    );

    connection.on(
      'PrintJobDispatched',
      (_payload: PrintJobDispatchedPayload) => {
        invalidateProductionQueries(queryClient);
      },
    );

    connection.on('PrintJobSendProgress', (payload: PrintJobSendProgress) => {
      if (payload.stage === 'Ended') {
        // The attempt stopped without sending; the job's own row says why.
        queryClient.resetQueries({
          queryKey: sendProgressKey(payload.printJobId),
        });
        invalidateProductionQueries(queryClient);
        return;
      }
      queryClient.setQueryData(sendProgressKey(payload.printJobId), payload);
    });

    return () => {
      stop();
    };
  }, [queryClient]);
}
