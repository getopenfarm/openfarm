import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { toast } from 'sonner';
import { ConfirmDialog } from '@/components/confirm-dialog/ConfirmDialog';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import { useOpenFarmApiEndpointsPrintersMarkPrinterReadyEndpoint } from '@/lib/api/generated/printers/printers';

export function useMarkPrinterReady() {
  const queryClient = useQueryClient();
  const [confirmationPrinterId, setConfirmationPrinterId] = useState<
    string | null
  >(null);
  const mutation = useOpenFarmApiEndpointsPrintersMarkPrinterReadyEndpoint({
    mutation: {
      onSuccess: () => {
        setConfirmationPrinterId(null);
        toast.success('Printer marked ready.');
        queryClient.invalidateQueries();
      },
      onError: async (error: unknown, variables) => {
        if (error instanceof Response && error.status === 409) {
          const problem = await error.clone().json();
          if (problem.title === 'Run completion confirmation required') {
            setConfirmationPrinterId(variables.id);
            return;
          }
        }
        toast.error(
          await extractErrorDetail(error, 'Could not mark printer ready.'),
        );
      },
    },
  });
  return {
    isPending: mutation.isPending,
    mutate: ({ id }: { id: string }) =>
      mutation.mutate({ id, data: { confirmRunCompletion: false } }),
    confirmationDialog: (
      <ConfirmDialog
        open={confirmationPrinterId !== null}
        onOpenChange={(open) => {
          if (!open) setConfirmationPrinterId(null);
        }}
        title="Confirm production run complete?"
        description="Every finite requirement has finished printing. Confirm the named run is complete and the printer is clear before marking it ready."
        confirmLabel="Confirm complete and mark ready"
        onConfirm={() => {
          if (confirmationPrinterId)
            mutation.mutate({
              id: confirmationPrinterId,
              data: { confirmRunCompletion: true },
            });
        }}
      />
    ),
  };
}
