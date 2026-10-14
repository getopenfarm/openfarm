import { useQueryClient } from '@tanstack/react-query';
import type { RefObject } from 'react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Spinner } from '@/components/ui/spinner';
import {
  getOpenFarmApiEndpointsSpoolsListSpoolsEndpointQueryKey,
  useOpenFarmApiEndpointsSpoolsUnloadSpoolEndpoint,
} from '@/lib/api/generated/spools/spools';

interface UnloadSpoolDialogProps {
  spoolId: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  returnFocusRef?: RefObject<HTMLButtonElement | null>;
}

export function UnloadSpoolDialog({
  spoolId,
  open,
  onOpenChange,
  returnFocusRef,
}: UnloadSpoolDialogProps) {
  const queryClient = useQueryClient();

  const unloadMutation = useOpenFarmApiEndpointsSpoolsUnloadSpoolEndpoint({
    mutation: {
      onSuccess: () => {
        onOpenChange(false);
        queryClient.invalidateQueries({
          queryKey: getOpenFarmApiEndpointsSpoolsListSpoolsEndpointQueryKey(),
        });
      },
      onError: () => {
        toast.error('Could not unload that spool.');
      },
    },
  });

  const pendingAction = unloadMutation.isPending
    ? unloadMutation.variables?.data.action
    : undefined;

  function handleAction(action: 'ReturnToStorage' | 'MarkFinished') {
    unloadMutation.mutate(
      { id: spoolId, data: { action } },
      {
        onSuccess: () => {
          toast.success(
            action === 'MarkFinished'
              ? 'Spool marked finished.'
              : 'Spool returned to storage.',
          );
        },
      },
    );
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        onCloseAutoFocus={(event) => {
          if (returnFocusRef?.current?.isConnected) {
            event.preventDefault();
            returnFocusRef.current.focus();
          }
        }}
      >
        <DialogHeader>
          <DialogTitle>Remove spool from Feed Slot</DialogTitle>
          <DialogDescription>
            Take this spool off its Feed Slot — return it to storage to reuse
            later, or mark it finished if it's empty.
          </DialogDescription>
        </DialogHeader>
        <DialogFooter>
          <DialogClose asChild>
            <Button type="button" variant="outline">
              Cancel
            </Button>
          </DialogClose>
          <Button
            type="button"
            variant="outline"
            disabled={unloadMutation.isPending}
            onClick={() => handleAction('ReturnToStorage')}
          >
            {pendingAction === 'ReturnToStorage' && (
              <Spinner data-icon="inline-start" />
            )}
            {pendingAction === 'ReturnToStorage'
              ? 'Returning…'
              : 'Return to storage'}
          </Button>
          <Button
            type="button"
            disabled={unloadMutation.isPending}
            onClick={() => handleAction('MarkFinished')}
          >
            {pendingAction === 'MarkFinished' && (
              <Spinner data-icon="inline-start" />
            )}
            {pendingAction === 'MarkFinished' ? 'Finishing…' : 'Mark finished'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
