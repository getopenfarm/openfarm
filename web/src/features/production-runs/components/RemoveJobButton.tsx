import { useQueryClient } from '@tanstack/react-query';
import { Trash2Icon } from 'lucide-react';
import { type RefObject, useState } from 'react';
import { toast } from 'sonner';
import { ConfirmDialog } from '@/components/confirm-dialog/ConfirmDialog';
import { Button } from '@/components/ui/button';
import { Spinner } from '@/components/ui/spinner';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import { useOpenFarmApiEndpointsPrintJobsCancelPrintJobEndpoint } from '@/lib/api/generated/print-jobs/print-jobs';

export function RemoveJobButton({
  jobId,
  open: controlledOpen,
  onOpenChange,
  hideTrigger = false,
  returnFocusRef,
  compact = false,
}: {
  jobId: string;
  open?: boolean;
  onOpenChange?: (open: boolean) => void;
  hideTrigger?: boolean;
  returnFocusRef?: RefObject<HTMLButtonElement | null>;
  /** Quiet ghost trigger for table rows; page headers use the destructive style. */
  compact?: boolean;
}) {
  const [localOpen, setLocalOpen] = useState(false);
  const open = controlledOpen ?? localOpen;
  const setOpen = onOpenChange ?? setLocalOpen;
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
        toast.error(await extractErrorDetail(error, 'Could not remove job.'));
      },
    },
  });

  return (
    <>
      {!hideTrigger && (
        <Button
          type="button"
          variant={compact ? 'ghost' : 'destructive'}
          size={compact ? 'sm' : 'default'}
          disabled={mutation.isPending}
          onClick={() => setOpen(true)}
        >
          {mutation.isPending ? (
            <Spinner data-icon="inline-start" />
          ) : (
            <Trash2Icon data-icon="inline-start" />
          )}
          {mutation.isPending ? 'Removing…' : 'Remove job'}
        </Button>
      )}
      <ConfirmDialog
        returnFocusRef={returnFocusRef}
        open={open}
        onOpenChange={setOpen}
        title="Remove this job?"
        description="Cancel the job and remove it from the queue. The job stays under Cancelled and in the production-run history."
        confirmLabel="Remove job"
        destructive
        isPending={mutation.isPending}
        onConfirm={() => mutation.mutate({ id: jobId })}
      />
    </>
  );
}
