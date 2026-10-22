import { useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import { useOpenFarmApiEndpointsSpoolsRestoreSpoolEndpoint } from '@/lib/api/generated/spools/spools';
import { invalidateFilamentTrackingQueries } from '@/lib/invalidateFilamentTrackingQueries';

/** A finished spool back to storage, with the grams finishing wrote off. */
export function useRestoreSpool() {
  const queryClient = useQueryClient();
  return useOpenFarmApiEndpointsSpoolsRestoreSpoolEndpoint({
    mutation: {
      onSuccess: () => {
        toast.success('Spool restored.');
        invalidateFilamentTrackingQueries(queryClient);
      },
      onError: async (error) => {
        toast.error(
          await extractErrorDetail(error, 'Could not restore spool.'),
        );
      },
    },
  });
}
