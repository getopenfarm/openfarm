import { useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import { getOpenFarmApiEndpointsFeedUnitsListFeedUnitsEndpointQueryKey } from '@/lib/api/generated/printers/printers';
import { useOpenFarmApiEndpointsSpoolsLoadSpoolEndpoint } from '@/lib/api/generated/spools/spools';

/** Load a spool into a Feed slot; refreshes spools and the printer's Feed units. */
export function useLoadSpool(printerId: string, onLoaded: () => void) {
  const queryClient = useQueryClient();
  return useOpenFarmApiEndpointsSpoolsLoadSpoolEndpoint({
    mutation: {
      onSuccess: (response) => {
        if (response.data?.rfidMismatchWarning) {
          toast.warning(
            'Spool loaded, but the detected filament does not match this spool.',
          );
        } else {
          toast.success('Spool loaded.');
        }
        onLoaded();
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
        toast.error(await extractErrorDetail(error, 'Could not load spool.'));
      },
    },
  });
}
