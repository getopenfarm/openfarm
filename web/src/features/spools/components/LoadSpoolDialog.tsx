import type { RefObject } from 'react';
import { useState } from 'react';
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
import { Field, FieldGroup, FieldLabel } from '@/components/ui/field';
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Spinner } from '@/components/ui/spinner';
import type {
  FeedUnitResponse,
  ListFeedUnitsResponse,
  ListFleetResponse,
} from '@/lib/api/generated/endpoints.schemas';
import {
  useOpenFarmApiEndpointsFeedUnitsListFeedUnitsEndpoint,
  useOpenFarmApiEndpointsPrintersListFleetEndpoint,
} from '@/lib/api/generated/printers/printers';
import { useLoadSpool } from '../hooks/useLoadSpool';

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
  returnFocusRef?: RefObject<HTMLButtonElement | null>;
}

export function LoadSpoolDialog({
  spoolId,
  open,
  onOpenChange,
  returnFocusRef,
}: LoadSpoolDialogProps) {
  const [printerId, setPrinterId] = useState('');
  const [feedSlotId, setFeedSlotId] = useState('');

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

  const loadMutation = useLoadSpool(printerId, () => {
    setPrinterId('');
    setFeedSlotId('');
    onOpenChange(false);
  });

  function handleSubmit() {
    if (!feedSlotId) return;
    loadMutation.mutate({ id: spoolId, data: { feedSlotId } });
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
          <DialogTitle>Load spool</DialogTitle>
          <DialogDescription>
            Choose a printer and a Feed slot.
          </DialogDescription>
        </DialogHeader>
        <FieldGroup className="flex flex-col gap-4">
          <Field className="flex flex-col gap-1.5">
            <FieldLabel htmlFor="load-printer">Printer</FieldLabel>
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
                <SelectGroup>
                  {printers.map((printer) => (
                    <SelectItem key={printer.id} value={printer.id ?? ''}>
                      {printer.name}
                    </SelectItem>
                  ))}
                </SelectGroup>
              </SelectContent>
            </Select>
          </Field>
          <Field data-disabled={!printerId} className="flex flex-col gap-1.5">
            <FieldLabel htmlFor="load-feed-slot">Feed slot</FieldLabel>
            <Select
              value={feedSlotId}
              onValueChange={setFeedSlotId}
              disabled={!printerId}
            >
              <SelectTrigger id="load-feed-slot" className="w-full">
                <SelectValue
                  placeholder={
                    printerId ? 'Choose a Feed slot' : 'Choose a printer first'
                  }
                />
              </SelectTrigger>
              <SelectContent>
                <SelectGroup>
                  {slotOptions.length === 0 ? (
                    <p className="px-3 py-2 text-sm text-muted-foreground">
                      No available Feed slots on this printer.
                    </p>
                  ) : (
                    slotOptions.map((option) => (
                      <SelectItem key={option.id} value={option.id}>
                        {option.label}
                      </SelectItem>
                    ))
                  )}
                </SelectGroup>
              </SelectContent>
            </Select>
          </Field>
        </FieldGroup>
        <DialogFooter>
          <DialogClose asChild>
            <Button type="button" variant="outline">
              Cancel
            </Button>
          </DialogClose>
          <Button
            type="button"
            onClick={handleSubmit}
            disabled={!feedSlotId || loadMutation.isPending}
          >
            {loadMutation.isPending && <Spinner data-icon="inline-start" />}
            {loadMutation.isPending ? 'Loading…' : 'Load spool'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
