import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { toast } from 'sonner';
import { ConfirmDialog } from '@/components/confirm-dialog/ConfirmDialog';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import {
  useOpenFarmApiEndpointsProductionRunsCancelProductionRunEndpoint,
  useOpenFarmApiEndpointsProductionRunsCloseProductionRunEndpoint,
  useOpenFarmApiEndpointsProductionRunsMoveRunToTopEndpoint,
} from '@/lib/api/generated/production-runs/production-runs';
import { invalidateProductionQueries } from './useProductionHub';

/** Move to top, close and cancel for one run at a time, shared by the list's row menu and the run page header. */
export function useRunActions() {
  const queryClient = useQueryClient();
  const [cancelTarget, setCancelTarget] = useState<{
    id: string;
    label: string;
  } | null>(null);

  function handlers(success: string, failure: string) {
    return {
      onSuccess: () => {
        toast.success(success);
        invalidateProductionQueries(queryClient);
      },
      onError: async (error: unknown) => {
        toast.error(await extractErrorDetail(error, failure));
      },
    };
  }

  const moveToTop = useOpenFarmApiEndpointsProductionRunsMoveRunToTopEndpoint({
    mutation: handlers('Run moved to top of queue.', 'Could not move run.'),
  });
  const close = useOpenFarmApiEndpointsProductionRunsCloseProductionRunEndpoint(
    { mutation: handlers('Run closed.', 'Could not close run.') },
  );
  const cancel =
    useOpenFarmApiEndpointsProductionRunsCancelProductionRunEndpoint({
      mutation: handlers('Run cancelled.', 'Could not cancel run.'),
    });

  return {
    moveToTop: (id: string) => moveToTop.mutate({ id }),
    close: (id: string) => close.mutate({ id }),
    /** Asks first: cancelling stops every plate not yet sent to a printer. */
    cancel: (id: string, label: string) => setCancelTarget({ id, label }),
    isPending: moveToTop.isPending || close.isPending || cancel.isPending,
    cancelDialog: (
      <ConfirmDialog
        open={cancelTarget !== null}
        onOpenChange={(open) => {
          if (!open) setCancelTarget(null);
        }}
        title={`Cancel ${cancelTarget?.label ?? 'run'}?`}
        description="Plates not yet sent to a printer are cancelled. Plates already printing finish and keep their outcome."
        confirmLabel="Cancel run"
        cancelLabel="Keep run"
        destructive
        isPending={cancel.isPending}
        onConfirm={() => {
          if (cancelTarget) cancel.mutate({ id: cancelTarget.id });
          setCancelTarget(null);
        }}
      />
    ),
  };
}
