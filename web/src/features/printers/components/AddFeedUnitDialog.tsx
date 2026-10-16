import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Field, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
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
} from '@/lib/api/generated/endpoints.schemas';
import {
  getOpenFarmApiEndpointsFeedUnitsListFeedUnitsEndpointQueryKey,
  useOpenFarmApiEndpointsFeedUnitsAddFeedUnitEndpoint,
} from '@/lib/api/generated/printers/printers';
import { httpClient } from '@/lib/api/httpClient';
import { configureReloader } from '../lib/reloaderApi';

type Kind = 'AutomaticFilamentReloader' | 'ExternalSpool';

interface AddFeedUnitDialogProps {
  printerId: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  unit?: FeedUnitResponse;
}

export function AddFeedUnitDialog({
  printerId,
  open,
  onOpenChange,
  unit,
}: AddFeedUnitDialogProps) {
  const [kind, setKind] = useState<Kind>('AutomaticFilamentReloader');
  const [name, setName] = useState('');
  const [slotCount, setSlotCount] = useState('2');
  const queryClient = useQueryClient();
  const [replaceExternal, setReplaceExternal] = useState(true);
  const [startingChannel, setStartingChannel] = useState('0');
  const [transferExternal, setTransferExternal] = useState(true);
  const topology = useQuery({
    queryKey: [
      `/api/v1/printers/${printerId}/feed-units`,
      { includeHidden: true },
    ],
    queryFn: () =>
      httpClient<{ data: ListFeedUnitsResponse }>(
        `/api/v1/printers/${printerId}/feed-units?includeHidden=true`,
      ),
    enabled: open,
  });
  const allUnits = topology.data?.data.feedUnits ?? [];
  const external = allUnits.find(
    (candidate) =>
      candidate.kind === 'ExternalSpool' &&
      (!candidate.supersededByFeedUnitId ||
        candidate.supersededByFeedUnitId === unit?.id ||
        allUnits.find((other) => other.id === candidate.supersededByFeedUnitId)
          ?.kind === 'MultiMaterialUnit'),
  );
  const externalSpool = external?.feedSlots?.find(
    (slot) => slot.currentSpool,
  )?.currentSpool;
  useEffect(() => {
    if (!open) return;
    setKind('AutomaticFilamentReloader');
    setName(unit?.displayLabel ?? '');
    setSlotCount(String(unit?.feedSlots?.length ?? 2));
    setReplaceExternal(unit ? Boolean(unit.replacesFeedUnitId) : true);
    setStartingChannel(
      String(
        unit?.feedSlots?.find((slot) => slot.id === unit.feedingSlotId)
          ?.slotPosition ?? 0,
      ),
    );
    setTransferExternal(true);
  }, [open, unit]);
  function refresh() {
    queryClient.invalidateQueries({
      queryKey: [`/api/v1/printers/${printerId}/feed-units`],
    });
    queryClient.invalidateQueries({
      predicate: (query) =>
        typeof query.queryKey[0] === 'string' &&
        query.queryKey[0].startsWith('/api/v1/spools'),
    });
  }
  const configureMutation = useMutation({
    mutationFn: () =>
      configureReloader(unit?.id ?? '', {
        replacesFeedUnitId:
          replaceExternal && external?.id ? external.id : null,
        feedingSlotId:
          unit?.feedSlots?.find(
            (slot) => slot.slotPosition === Number(startingChannel),
          )?.id ?? null,
        transferExternalSpool: transferExternal,
      }),
    onSuccess: () => {
      toast.success('Reloader saved.');
      refresh();
      onOpenChange(false);
    },
    onError: () =>
      toast.error(
        'Could not save reloader. Check for an occupied channel or port.',
      ),
  });

  const addMutation = useOpenFarmApiEndpointsFeedUnitsAddFeedUnitEndpoint({
    mutation: {
      onSuccess: () => {
        toast.success(
          kind === 'AutomaticFilamentReloader'
            ? 'Automatic filament reloader added.'
            : 'External spool added.',
        );
        refresh();
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
        toast.error('Could not add feed unit.');
      },
    },
  });

  const canSubmit =
    kind === 'ExternalSpool' ||
    (name.trim().length > 0 &&
      Number.isInteger(Number(slotCount)) &&
      Number(slotCount) >= 1 &&
      Number(slotCount) <= 100 &&
      Number(startingChannel) >= 0 &&
      Number(startingChannel) < Number(slotCount) &&
      !topology.isLoading &&
      !topology.isError);

  function handleSubmit() {
    if (unit) {
      configureMutation.mutate();
      return;
    }
    addMutation.mutate({
      printerId,
      data:
        kind === 'AutomaticFilamentReloader'
          ? {
              kind,
              name: name.trim(),
              slotCount: Number(slotCount),
              startingSlotPosition: Number(startingChannel),
              replacesFeedUnitId: replaceExternal
                ? (external?.id ?? null)
                : null,
              transferExternalSpool: transferExternal,
            }
          : { kind },
    });
  }

  const isSaving = addMutation.isPending || configureMutation.isPending;

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{unit ? 'Edit reloader' : 'Add feed unit'}</DialogTitle>
          <DialogDescription>
            {unit
              ? 'Choose the feed path and the feeding channel.'
              : 'Add a reloader or an external spool.'}
          </DialogDescription>
        </DialogHeader>
        <div className="flex flex-col gap-4">
          {!unit && (
            <Field className="flex flex-col gap-1.5">
              <FieldLabel htmlFor="feed-unit-kind">Kind</FieldLabel>
              <Select
                value={kind}
                onValueChange={(value) => setKind(value as Kind)}
              >
                <SelectTrigger id="feed-unit-kind" className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectGroup>
                    <SelectItem value="AutomaticFilamentReloader">
                      Automatic filament reloader
                    </SelectItem>
                    <SelectItem value="ExternalSpool">
                      External spool
                    </SelectItem>
                  </SelectGroup>
                </SelectContent>
              </Select>
            </Field>
          )}
          {kind === 'AutomaticFilamentReloader' ? (
            <>
              <Field
                data-disabled={Boolean(unit)}
                className="flex flex-col gap-1.5"
              >
                <FieldLabel htmlFor="feed-unit-name">Name</FieldLabel>
                <Input
                  disabled={Boolean(unit)}
                  id="feed-unit-name"
                  value={name}
                  onChange={(event) => setName(event.target.value)}
                  placeholder="e.g. Splicer"
                />
              </Field>
              <Field
                data-disabled={Boolean(unit)}
                className="flex flex-col gap-1.5"
              >
                <FieldLabel htmlFor="feed-unit-slot-count">
                  Slot count
                </FieldLabel>
                <Input
                  disabled={Boolean(unit)}
                  id="feed-unit-slot-count"
                  type="number"
                  min={1}
                  value={slotCount}
                  onChange={(event) => setSlotCount(event.target.value)}
                />
              </Field>
              <Field className="flex flex-col gap-1.5">
                <FieldLabel htmlFor="reloader-start-channel">
                  Feeding channel
                </FieldLabel>
                <Select
                  value={startingChannel}
                  onValueChange={setStartingChannel}
                >
                  <SelectTrigger id="reloader-start-channel">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectGroup>
                      {Array.from(
                        {
                          length: Math.min(
                            100,
                            Math.max(0, Number(slotCount) || 0),
                          ),
                        },
                        (_, position) => position,
                      ).map((position) => (
                        <SelectItem key={position} value={String(position)}>
                          Channel {position + 1}
                        </SelectItem>
                      ))}
                    </SelectGroup>
                  </SelectContent>
                </Select>
                <p className="text-xs text-muted-foreground">
                  Select the physical feeding channel. The continuation from
                  recorded depletion is estimated. You can correct it.
                </p>
              </Field>
              {external && (
                <Field
                  orientation="horizontal"
                  className="flex items-center gap-2"
                >
                  <Checkbox
                    id="replace-external"
                    checked={replaceExternal}
                    onCheckedChange={(value) =>
                      setReplaceExternal(value === true)
                    }
                  />
                  <FieldLabel htmlFor="replace-external">
                    Feeds through external spool port
                  </FieldLabel>
                </Field>
              )}
              {replaceExternal && externalSpool && (
                <Field
                  orientation="horizontal"
                  className="flex items-center gap-2"
                >
                  <Checkbox
                    id="transfer-external"
                    checked={transferExternal}
                    onCheckedChange={(value) =>
                      setTransferExternal(value === true)
                    }
                  />
                  <FieldLabel htmlFor="transfer-external">
                    Move external spool to the feeding channel
                  </FieldLabel>
                </Field>
              )}
              {replaceExternal && externalSpool && !transferExternal && (
                <p className="text-xs text-muted-foreground">
                  The external spool returns to inventory. Its remaining weight
                  stays the same.
                </p>
              )}
              {!external && !topology.isLoading && !topology.isError && (
                <p className="text-xs text-muted-foreground">
                  No external port available. The reloader uses an independent
                  feed path.
                </p>
              )}
              {topology.isError && (
                <p role="alert">
                  Could not load feed paths.{' '}
                  <Button variant="outline" onClick={() => topology.refetch()}>
                    Retry
                  </Button>
                </p>
              )}
            </>
          ) : null}
        </div>
        <DialogFooter>
          <DialogClose asChild>
            <Button type="button" variant="outline">
              Cancel
            </Button>
          </DialogClose>
          <Button
            type="button"
            onClick={handleSubmit}
            disabled={!canSubmit || isSaving}
          >
            {isSaving && <Spinner data-icon="inline-start" />}
            {unit
              ? isSaving
                ? 'Saving…'
                : 'Save'
              : isSaving
                ? 'Adding…'
                : 'Add feed unit'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
