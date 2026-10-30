import { useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import type { PartProcessingJobResponse } from '@/lib/api/generated/endpoints.schemas';
import { getOpenFarmApiEndpointsPartProcessingJobsGetPartProcessingJobEndpointQueryKey } from '@/lib/api/generated/part-processing-jobs/part-processing-jobs';
import { getOpenFarmApiEndpointsPartsGetPartEndpointQueryKey } from '@/lib/api/generated/parts/parts';
import { connectFarmHub } from '@/lib/signalr/farmHubConnection';

interface PartProcessingJobStatusChangedPayload {
  jobId: string;
  status: string;
  partId?: string | null;
  errorMessage?: string | null;
  preview?: PartProcessingJobResponse['preview'];
}

interface PartUpsertedPayload {
  partId: string;
}

interface PartEstimateResolvedPayload {
  partId: string;
}

interface PartEstimateUnavailablePayload {
  partId: string;
}

interface PartDeletedPayload {
  partId: string;
}

type JobQueryData = {
  data: PartProcessingJobResponse;
  status: number;
  headers: Headers;
};

/** Matches every list/detail query this feature has cached — parts list, tag list, and every
 * individual part/job detail — so a broad "something about the catalog changed" event (confirm,
 * estimate resolved/unavailable, delete) can invalidate everything relevant in one predicate,
 * the same shape useFarmHub already uses for printer/filament-tracking queries. */
function invalidatePartQueries(queryClient: ReturnType<typeof useQueryClient>) {
  queryClient.invalidateQueries({
    predicate: (query) =>
      typeof query.queryKey[0] === 'string' &&
      (query.queryKey[0] === '/api/v1/library' ||
        query.queryKey[0].startsWith('/api/v1/parts') ||
        query.queryKey[0].startsWith('/api/v1/part-processing-jobs') ||
        query.queryKey[0].startsWith('/api/v1/tags')),
  });
}

/**
 * Opens the /hubs/farm SignalR connection and writes Part Library events directly into TanStack
 * Query's cache — no polling fallback, matching every other real-time feature in this app.
 *
 * `PartProcessingJobStatusChanged` carries the derived preview directly in the push
 * (contracts/part-library-api.md), so the job-detail cache is written in place first — the review
 * panel updates at once — and then refetched for the fields the push leaves out. `PartUpserted`/`PartEstimateResolved`/`PartEstimateUnavailable`/`PartDeleted`
 * invalidate instead, since those always mean "go re-fetch the authoritative Part row."
 */
export function usePartsHub() {
  const queryClient = useQueryClient();

  useEffect(() => {
    const { connection, stop } = connectFarmHub({
      onConnected: () => invalidatePartQueries(queryClient),
      onReconnected: () => invalidatePartQueries(queryClient),
    });

    connection.on(
      'PartProcessingJobStatusChanged',
      (payload: PartProcessingJobStatusChangedPayload) => {
        const jobKey =
          getOpenFarmApiEndpointsPartProcessingJobsGetPartProcessingJobEndpointQueryKey(
            payload.jobId,
          );
        queryClient.setQueryData<JobQueryData | undefined>(
          jobKey,
          (current) => {
            const base: PartProcessingJobResponse = current?.data ?? {
              id: payload.jobId,
            };
            return {
              ...(current ?? { status: 200, headers: new Headers() }),
              data: {
                ...base,
                id: payload.jobId,
                status: payload.status,
                partId: payload.partId ?? base.partId ?? null,
                errorMessage: payload.errorMessage ?? null,
                preview: payload.preview ?? base.preview ?? null,
              },
            };
          },
        );
        // The push is a partial preview (no estimation source, warnings or print-requirements status), so refetch the rest.
        queryClient.invalidateQueries({ queryKey: jobKey });
      },
    );

    connection.on('PartUpserted', (payload: PartUpsertedPayload) => {
      invalidatePartQueries(queryClient);
      queryClient.invalidateQueries({
        queryKey: getOpenFarmApiEndpointsPartsGetPartEndpointQueryKey(
          payload.partId,
        ),
      });
    });

    connection.on(
      'PartEstimateResolved',
      (payload: PartEstimateResolvedPayload) => {
        invalidatePartQueries(queryClient);
        queryClient.invalidateQueries({
          queryKey: getOpenFarmApiEndpointsPartsGetPartEndpointQueryKey(
            payload.partId,
          ),
        });
      },
    );

    connection.on(
      'PartEstimateUnavailable',
      (payload: PartEstimateUnavailablePayload) => {
        invalidatePartQueries(queryClient);
        queryClient.invalidateQueries({
          queryKey: getOpenFarmApiEndpointsPartsGetPartEndpointQueryKey(
            payload.partId,
          ),
        });
      },
    );

    connection.on('PartDeleted', (_payload: PartDeletedPayload) => {
      invalidatePartQueries(queryClient);
    });

    return () => {
      stop();
    };
  }, [queryClient]);
}
