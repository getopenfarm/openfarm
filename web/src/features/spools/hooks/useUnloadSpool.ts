import { useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import { getOpenFarmApiEndpointsFeedUnitsListFeedUnitsEndpointQueryKey } from '@/lib/api/generated/printers/printers';
import { useOpenFarmApiEndpointsSpoolsUnloadSpoolEndpoint } from '@/lib/api/generated/spools/spools';

/** Unload a spool from its Feed slot; refreshes spools and the printer's Feed units. */
export function useUnloadSpool(printerId: string, onUnloaded?: () => void) {
  const queryClient = useQueryClient();
  return useOpenFarmApiEndpointsSpoolsUnloadSpoolEndpoint({
    mutation: {
      onSuccess: (_response, variables) => {
        toast.success(
          variables.data.action === 'MarkFinished'
            ? 'Spool marked finished.'
            : 'Spool returned to storage.',
        );
        onUnloaded?.();
        queryClient.invalidateQueries({
          predicate: (query) =>
            typeof query.queryKey[0] === 'string' &&
            query.queryKey[0].startsWith('/api/v1/spools'),
        });
        queryClient.invalidateQueries({
          queryKey:
            getOpenFarmApiEndpointsFeedUnitsListFeedUnitsEndpointQueryKey(
              printerId,
            ),
        });
      },
      onError: async (error) => {
        toast.error(await extractErrorDetail(error, 'Could not unload spool.'));
      },
    },
  });
}
