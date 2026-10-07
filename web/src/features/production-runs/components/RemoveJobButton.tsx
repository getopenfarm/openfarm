import { useQueryClient } from '@tanstack/react-query';
import { Trash2Icon } from 'lucide-react';
import { useState } from 'react';
import { toast } from 'sonner';
import { ConfirmDialog } from '@/components/confirm-dialog/ConfirmDialog';
import { Button } from '@/components/ui/button';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import { useOpenFarmApiEndpointsPrintJobsCancelPrintJobEndpoint } from '@/lib/api/generated/print-jobs/print-jobs';

export function RemoveJobButton({ jobId }: { jobId: string }) {
  const [open, setOpen] = useState(false);
  const queryClient = useQueryClient();
  const mutation = useOpenFarmApiEndpointsPrintJobsCancelPrintJobEndpoint({
    mutation: {
      onSuccess: (response) => {
        toast.success(
          response.data?.status === 'Cancelled'
            ? 'Job removed from the queue.'
            : 'Cancellation requested. Waiting for printer confirmation.',
        );
        queryClient.invalidateQueries({
          predicate: (query) =>
            typeof query.queryKey[0] === 'string' &&
            (query.queryKey[0].startsWith('/api/v1/print-jobs') ||
              query.queryKey[0].startsWith('/api/v1/production-runs')),
        });
      },
      onError: async (error) => {
        toast.error(
          await extractErrorDetail(error, 'Could not remove this job.'),
        );
      },
    },
  });

  return (
    <>
      <Button
        type="button"
        variant="ghost"
        size="sm"
        disabled={mutation.isPending}
        onClick={() => setOpen(true)}
      >
        <Trash2Icon className="size-3.5" />
        {mutation.isPending ? 'Removing…' : 'Remove job'}
      </Button>
      <ConfirmDialog
        open={open}
        onOpenChange={setOpen}
        title="Remove this job?"
        description="This job will be cancelled and removed from the print queue. Its production-run history will be kept."
        confirmLabel="Remove job"
        onConfirm={() => mutation.mutate({ id: jobId })}
      />
    </>
  );
}
