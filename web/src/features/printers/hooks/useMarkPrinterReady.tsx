import { useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import { useOpenFarmApiEndpointsPrintersMarkPrinterReadyEndpoint } from '@/lib/api/generated/printers/printers';

/** Clears the plate only: a run completes on its own and is signed off by Close on its page. */
export function useMarkPrinterReady() {
  const queryClient = useQueryClient();
  const mutation = useOpenFarmApiEndpointsPrintersMarkPrinterReadyEndpoint({
    mutation: {
      onSuccess: () => {
        toast.success('Printer marked ready.');
        queryClient.invalidateQueries();
      },
      onError: async (error: unknown) => {
        toast.error(
          await extractErrorDetail(error, 'Could not mark printer ready.'),
        );
      },
    },
  });
  return {
    isPending: mutation.isPending,
    mutate: ({ id }: { id: string }) => mutation.mutate({ id }),
  };
}
