import { useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import { useOpenFarmApiEndpointsPrintersMarkPrinterNotReadyEndpoint } from '@/lib/api/generated/printers/printers';

/** An operator hold on a Ready printer: it takes no job until Mark ready. */
export function useMarkPrinterNotReady() {
  const queryClient = useQueryClient();
  const mutation = useOpenFarmApiEndpointsPrintersMarkPrinterNotReadyEndpoint({
    mutation: {
      onSuccess: () => {
        toast.success('Printer marked not ready.');
        queryClient.invalidateQueries();
      },
      onError: async (error: unknown) => {
        toast.error(
          await extractErrorDetail(error, 'Could not mark printer not ready.'),
        );
      },
    },
  });
  return {
    isPending: mutation.isPending,
    mutate: ({ id }: { id: string }) => mutation.mutate({ id }),
  };
}
