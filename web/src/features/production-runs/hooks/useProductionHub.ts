import { useQueryClient } from '@tanstack/react-query';
import {
  useFarmHubEvent,
  useOnFarmHubReconnected,
} from '@/lib/signalr/farmHubConnection';
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
        query.queryKey[0].startsWith('/api/v1/print-jobs') ||
        query.queryKey[0].startsWith('/api/v1/production-demands')),
  });
}

/**
 * Invalidates the affected production-run/print-job queries on every push, on the tab's shared hub connection — no
 * polling fallback, matching every other real-time feature in this app. Status is always re-fetched (never optimistically
 * written from the push payload alone) since a live-integrated job's remote-control actions are deliberately
 * request/confirm, not optimistic (research.md R12, FR-024a) — the client always re-reads the server's own state.
 */
export function useProductionHub(
  onJobStatusChanged?: (jobId: string, status: string) => void,
) {
  const queryClient = useQueryClient();
  const invalidate = () => invalidateProductionQueries(queryClient);

  useOnFarmHubReconnected(invalidate);
  useFarmHubEvent(
    'ProductionRunStatusChanged',
    (_payload: ProductionRunStatusChangedPayload) => invalidate(),
  );
  useFarmHubEvent(
    'PrintJobStatusChanged',
    (payload: PrintJobStatusChangedPayload) => {
      onJobStatusChanged?.(payload.printJobId, payload.status);
      queryClient.resetQueries({
        queryKey: sendProgressKey(payload.printJobId),
      });
      invalidate();
    },
  );
  useFarmHubEvent('ProductionDemandChanged', invalidate);
  useFarmHubEvent('PartDeleted', invalidate);
  useFarmHubEvent('PrintJobDispatched', (_payload: PrintJobDispatchedPayload) =>
    invalidate(),
  );
  useFarmHubEvent('PrintJobSendProgress', (payload: PrintJobSendProgress) => {
    if (payload.stage === 'Ended') {
      // The attempt stopped without sending; the job's own row says why.
      queryClient.resetQueries({
        queryKey: sendProgressKey(payload.printJobId),
      });
      invalidate();
      return;
    }
    queryClient.setQueryData(sendProgressKey(payload.printJobId), payload);
  });
}
