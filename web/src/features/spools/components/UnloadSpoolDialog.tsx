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
import { useOpenFarmApiEndpointsSpoolsUnloadSpoolEndpoint } from '@/lib/api/generated/spools/spools';
import { invalidateFilamentTrackingQueries } from '@/lib/invalidateFilamentTrackingQueries';

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
        invalidateFilamentTrackingQueries(queryClient);
      },
      onError: () => {
        toast.error('Could not unload spool.');
      },
    },
  });

  function returnToStorage() {
    unloadMutation.mutate(
      { id: spoolId, data: { action: 'ReturnToStorage' } },
      { onSuccess: () => toast.success('Spool returned to storage.') },
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
          <DialogTitle>Unload spool from feed slot</DialogTitle>
          <DialogDescription>Return the spool to storage.</DialogDescription>
        </DialogHeader>
        <DialogFooter>
          <DialogClose asChild>
            <Button type="button" variant="outline">
              Cancel
            </Button>
          </DialogClose>
          <Button
            type="button"
            disabled={unloadMutation.isPending}
            onClick={returnToStorage}
          >
            {unloadMutation.isPending && <Spinner data-icon="inline-start" />}
            {unloadMutation.isPending ? 'Returning…' : 'Return to storage'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
