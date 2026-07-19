import { useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import {
  getOpenFarmApiEndpointsSpoolsListSpoolsEndpointQueryKey,
  useOpenFarmApiEndpointsSpoolsUnloadSpoolEndpoint,
} from '@/lib/api/generated/spools/spools';

interface UnloadSpoolDialogProps {
  spoolId: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function UnloadSpoolDialog({
  spoolId,
  open,
  onOpenChange,
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
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Remove spool from Feed Slot</DialogTitle>
          <DialogDescription>
            Take this spool off its Feed Slot — return it to storage to reuse
            later, or mark it finished if it's empty.
          </DialogDescription>
        </DialogHeader>
        <DialogFooter>
          <Button
            type="button"
            variant="outline"
            disabled={unloadMutation.isPending}
            onClick={() => handleAction('ReturnToStorage')}
          >
            Return to storage
          </Button>
          <Button
            type="button"
            disabled={unloadMutation.isPending}
            onClick={() => handleAction('MarkFinished')}
          >
            Mark finished
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
