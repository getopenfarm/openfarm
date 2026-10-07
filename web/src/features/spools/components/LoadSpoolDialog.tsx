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
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import type {
  FeedUnitResponse,
  ListFeedUnitsResponse,
  ListFleetResponse,
} from '@/lib/api/generated/endpoints.schemas';
import {
  useOpenFarmApiEndpointsFeedUnitsListFeedUnitsEndpoint,
  useOpenFarmApiEndpointsPrintersListFleetEndpoint,
} from '@/lib/api/generated/printers/printers';
import {
  getOpenFarmApiEndpointsSpoolsListSpoolsEndpointQueryKey,
  useOpenFarmApiEndpointsSpoolsLoadSpoolEndpoint,
} from '@/lib/api/generated/spools/spools';

function flattenAvailableSlots(feedUnits: FeedUnitResponse[]) {
  return feedUnits.flatMap((unit) =>
    (unit.feedSlots ?? [])
      .filter((slot) => !slot.deactivatedAtUtc && !slot.currentSpool)
      .map((slot) => ({
        id: slot.id ?? '',
        label: `${unit.displayLabel} · ${slot.displayLabel}`,
      })),
  );
}

interface LoadSpoolDialogProps {
  spoolId: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function LoadSpoolDialog({
  spoolId,
  open,
  onOpenChange,
}: LoadSpoolDialogProps) {
  const [printerId, setPrinterId] = useState('');
  const [feedSlotId, setFeedSlotId] = useState('');
  const queryClient = useQueryClient();

  const fleetQuery = useOpenFarmApiEndpointsPrintersListFleetEndpoint();
  const printers =
    (fleetQuery.data?.data as ListFleetResponse | undefined)?.printers ?? [];

  const feedUnitsQuery = useOpenFarmApiEndpointsFeedUnitsListFeedUnitsEndpoint(
    printerId,
    undefined,
    { query: { enabled: Boolean(printerId) } },
  );
  const feedUnits =
    (feedUnitsQuery.data?.data as ListFeedUnitsResponse | undefined)
      ?.feedUnits ?? [];
  const slotOptions = flattenAvailableSlots(feedUnits);

  const loadMutation = useOpenFarmApiEndpointsSpoolsLoadSpoolEndpoint({
    mutation: {
      onSuccess: (response) => {
        const body = response.data;
        if (body?.rfidMismatchWarning) {
          toast.warning(
            'Spool loaded, but the detected filament does not match this spool.',
          );
        } else {
          toast.success('Spool loaded.');
        }
        setPrinterId('');
        setFeedSlotId('');
        onOpenChange(false);
        queryClient.invalidateQueries({
          queryKey: getOpenFarmApiEndpointsSpoolsListSpoolsEndpointQueryKey(),
        });
      },
      onError: () => {
        toast.error('Could not load spool onto that Feed Slot.');
      },
    },
  });

  function handleSubmit() {
    if (!feedSlotId) return;
    loadMutation.mutate({ id: spoolId, data: { feedSlotId } });
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Load spool</DialogTitle>
          <DialogDescription>
            Choose the printer and Feed Slot this spool is being loaded onto.
          </DialogDescription>
        </DialogHeader>
        <div className="flex flex-col gap-4">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="load-printer">Printer</Label>
            <Select
              value={printerId}
              onValueChange={(value) => {
                setPrinterId(value);
                setFeedSlotId('');
              }}
            >
              <SelectTrigger id="load-printer" className="w-full">
                <SelectValue placeholder="Choose a printer" />
              </SelectTrigger>
              <SelectContent>
                {printers.map((printer) => (
                  <SelectItem key={printer.id} value={printer.id ?? ''}>
                    {printer.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="load-feed-slot">Feed Slot</Label>
            <Select
              value={feedSlotId}
              onValueChange={setFeedSlotId}
              disabled={!printerId}
            >
              <SelectTrigger id="load-feed-slot" className="w-full">
                <SelectValue
                  placeholder={
                    printerId ? 'Choose a Feed Slot' : 'Choose a printer first'
                  }
                />
              </SelectTrigger>
              <SelectContent>
                {slotOptions.length === 0 ? (
                  <p className="px-3 py-2 text-sm text-muted-foreground">
                    No available Feed Slots on this printer.
                  </p>
                ) : (
                  slotOptions.map((option) => (
                    <SelectItem key={option.id} value={option.id}>
                      {option.label}
                    </SelectItem>
                  ))
                )}
              </SelectContent>
            </Select>
          </div>
        </div>
        <DialogFooter>
          <Button
            type="button"
            onClick={handleSubmit}
            disabled={!feedSlotId || loadMutation.isPending}
          >
            {loadMutation.isPending ? 'Loading…' : 'Load spool'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
