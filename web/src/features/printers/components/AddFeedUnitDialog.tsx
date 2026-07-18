import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
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
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  getOpenFarmApiEndpointsFeedUnitsListFeedUnitsEndpointQueryKey,
  useOpenFarmApiEndpointsFeedUnitsAddFeedUnitEndpoint,
} from '@/lib/api/generated/printers/printers';

type Kind = 'AutomaticFilamentReloader' | 'ExternalSpool';

interface AddFeedUnitDialogProps {
  printerId: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function AddFeedUnitDialog({
  printerId,
  open,
  onOpenChange,
}: AddFeedUnitDialogProps) {
  const [kind, setKind] = useState<Kind>('AutomaticFilamentReloader');
  const [name, setName] = useState('');
  const [slotCount, setSlotCount] = useState('2');
  const queryClient = useQueryClient();

  const addMutation = useOpenFarmApiEndpointsFeedUnitsAddFeedUnitEndpoint({
    mutation: {
      onSuccess: () => {
        toast.success(
          kind === 'AutomaticFilamentReloader'
            ? 'Automatic Filament Reloader added.'
            : 'External Spool revealed.',
        );
        setName('');
        setSlotCount('2');
        onOpenChange(false);
        queryClient.invalidateQueries({
          queryKey:
            getOpenFarmApiEndpointsFeedUnitsListFeedUnitsEndpointQueryKey(
              printerId,
            ),
        });
      },
      onError: () => {
        toast.error('Could not add that Feed Unit.');
      },
    },
  });

  const canSubmit =
    kind === 'ExternalSpool' ||
    (name.trim().length > 0 && Number(slotCount) >= 1);

  function handleSubmit() {
    addMutation.mutate({
      printerId,
      data:
        kind === 'AutomaticFilamentReloader'
          ? { kind, name: name.trim(), slotCount: Number(slotCount) }
          : { kind },
    });
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Add a Feed Unit</DialogTitle>
          <DialogDescription>
            Declare an Automatic Filament Reloader, or reveal this printer's
            External Spool if it's hidden.
          </DialogDescription>
        </DialogHeader>
        <div className="flex flex-col gap-4">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="feed-unit-kind">Kind</Label>
            <Select
              value={kind}
              onValueChange={(value) => setKind(value as Kind)}
            >
              <SelectTrigger id="feed-unit-kind" className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="AutomaticFilamentReloader">
                  Automatic Filament Reloader
                </SelectItem>
                <SelectItem value="ExternalSpool">External Spool</SelectItem>
              </SelectContent>
            </Select>
          </div>
          {kind === 'AutomaticFilamentReloader' ? (
            <>
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="feed-unit-name">Name</Label>
                <Input
                  id="feed-unit-name"
                  value={name}
                  onChange={(event) => setName(event.target.value)}
                  placeholder="e.g. Splicer"
                />
              </div>
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="feed-unit-slot-count">Slot count</Label>
                <Input
                  id="feed-unit-slot-count"
                  type="number"
                  min={1}
                  value={slotCount}
                  onChange={(event) => setSlotCount(event.target.value)}
                />
              </div>
            </>
          ) : null}
        </div>
        <DialogFooter>
          <Button
            type="button"
            onClick={handleSubmit}
            disabled={!canSubmit || addMutation.isPending}
          >
            {addMutation.isPending ? 'Adding…' : 'Add Feed Unit'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
