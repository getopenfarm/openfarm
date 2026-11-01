import { keepPreviousData } from '@tanstack/react-query';
import type { RefObject } from 'react';
import { useState } from 'react';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { PermissionButton as Button } from '@/components/PermissionButton';
import { Swatch } from '@/components/swatch/Swatch';
import {
  Dialog,
  DialogClose,
  DialogDescription,
  DialogScrollableContent,
  DialogTitle,
} from '@/components/ui/dialog';
import { Spinner } from '@/components/ui/spinner';
import { useFilamentCatalog } from '@/features/filament/data/useFilamentCatalog';
import { useDebouncedValue } from '@/hooks/useDebouncedValue';
import {
  type ListFeedUnitsResponse,
  type ListSpoolsResponse,
  SpoolState,
} from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsFeedUnitsListFeedUnitsEndpoint } from '@/lib/api/generated/printers/printers';
import { useOpenFarmApiEndpointsSpoolsListSpoolsEndpoint } from '@/lib/api/generated/spools/spools';
import { formatGrams } from '@/lib/formatUnits';
import { useLoadSpool } from '../hooks/useLoadSpool';
import { useUnloadSpool } from '../hooks/useUnloadSpool';
import { FillMeter } from './FillMeter';
import { SpoolPicker } from './SpoolPicker';

/** Spools fetched per search; a longer match list asks the operator to narrow the search. */
const PICKER_PAGE_SIZE = 50;

interface ManageSlotDialogProps {
  printerId: string;
  feedSlotId: string;
  slotLabel: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  returnFocusRef?: RefObject<HTMLButtonElement | null>;
}

/**
 * Everything you can do to one Feed slot: unload what is in it, or find a spool and load it.
 * The view follows the live slot, so after an unload it switches to the picker by itself.
 */
export function ManageSlotDialog({
  printerId,
  feedSlotId,
  slotLabel,
  open,
  onOpenChange,
  returnFocusRef,
}: ManageSlotDialogProps) {
  const [spoolId, setSpoolId] = useState('');
  const [query, setQuery] = useState('');
  const search = useDebouncedValue(query.trim(), 250);
  const catalog = useFilamentCatalog();

  const feedUnitsQuery = useOpenFarmApiEndpointsFeedUnitsListFeedUnitsEndpoint(
    printerId,
    undefined,
    { query: { enabled: open && Boolean(printerId) } },
  );
  const units =
    (feedUnitsQuery.data?.data as ListFeedUnitsResponse | undefined)
      ?.feedUnits ?? [];
  const unit = units.find((candidate) =>
    candidate.feedSlots?.some((slot) => slot.id === feedSlotId),
  );
  const slot = unit?.feedSlots?.find(
    (candidate) => candidate.id === feedSlotId,
  );
  const current = slot?.currentSpool;
  const filament = catalog.find((candidate) =>
    candidate.variants?.some(
      (variant) => variant.id === current?.filamentVariantId,
    ),
  );
  const variant = filament?.variants?.find(
    (candidate) => candidate.id === current?.filamentVariantId,
  );
  const feeding =
    unit?.kind === 'AutomaticFilamentReloader' &&
    unit.feedingSlotId === feedSlotId;

  const loadMutation = useLoadSpool(printerId, () => {
    setSpoolId('');
    setQuery('');
    onOpenChange(false);
  });
  const unloadMutation = useUnloadSpool(printerId);
  const pendingUnload = unloadMutation.isPending
    ? unloadMutation.variables?.data.action
    : undefined;
  // Keep actions locked until the slot refetches, or a second click would hit a stale view.
  const busy = unloadMutation.isPending || feedUnitsQuery.isFetching;

  const loading = feedUnitsQuery.isLoading;
  const failed = feedUnitsQuery.isError || (!loading && !slot);
  const view = loading
    ? 'loading'
    : failed
      ? 'error'
      : current
        ? 'loaded'
        : 'empty';

  // Only an empty slot needs the picker; state and search are applied by the server.
  const spoolsQuery = useOpenFarmApiEndpointsSpoolsListSpoolsEndpoint(
    {
      state: SpoolState.InStorage,
      search: search || undefined,
      page: 1,
      pageSize: PICKER_PAGE_SIZE,
    },
    {
      query: {
        enabled: open && view === 'empty',
        placeholderData: keepPreviousData,
      },
    },
  );
  const listing = spoolsQuery.data?.data as ListSpoolsResponse | undefined;
  const candidates = listing?.spools ?? [];

  const capacity = current?.spoolWeightGrams ?? 0;
  const remaining = current?.remainingWeightGrams;

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogScrollableContent
        className="sm:max-w-lg"
        onCloseAutoFocus={(event) => {
          if (returnFocusRef?.current?.isConnected) {
            event.preventDefault();
            returnFocusRef.current.focus();
          }
        }}
        header={
          <>
            <DialogTitle>{slotLabel}</DialogTitle>
            <DialogDescription>
              {view === 'loaded'
                ? 'Unload to change the spool, or mark it finished if it is empty.'
                : view === 'empty'
                  ? 'Empty. Choose a spool to load.'
                  : 'Feed slot'}
            </DialogDescription>
          </>
        }
        footer={
          view === 'loaded' ? (
            <>
              <DialogClose asChild>
                <Button type="button" variant="outline">
                  Close
                </Button>
              </DialogClose>
              <Button
                type="button"
                variant="outline"
                disabled={busy}
                onClick={() =>
                  unloadMutation.mutate({
                    id: current?.id ?? '',
                    data: { action: 'ReturnToStorage' },
                  })
                }
              >
                {pendingUnload === 'ReturnToStorage' && (
                  <Spinner data-icon="inline-start" />
                )}
                {pendingUnload === 'ReturnToStorage'
                  ? 'Returning…'
                  : 'Return to storage'}
              </Button>
              <Button
                type="button"
                disabled={busy}
                onClick={() =>
                  unloadMutation.mutate({
                    id: current?.id ?? '',
                    data: { action: 'MarkFinished' },
                  })
                }
              >
                {pendingUnload === 'MarkFinished' && (
                  <Spinner data-icon="inline-start" />
                )}
                {pendingUnload === 'MarkFinished'
                  ? 'Finishing…'
                  : 'Mark finished'}
              </Button>
            </>
          ) : (
            <>
              <DialogClose asChild>
                <Button type="button" variant="outline">
                  Cancel
                </Button>
              </DialogClose>
              {view === 'empty' && (
                <Button
                  permission="spools:load"
                  type="button"
                  disabled={!spoolId || loadMutation.isPending}
                  onClick={() =>
                    loadMutation.mutate({ id: spoolId, data: { feedSlotId } })
                  }
                >
                  {loadMutation.isPending && (
                    <Spinner data-icon="inline-start" />
                  )}
                  {loadMutation.isPending ? 'Loading…' : 'Load spool'}
                </Button>
              )}
            </>
          )
        }
      >
        {view === 'loading' && (
          <div role="status">
            <EmptyState
              size="sm"
              bordered={false}
              description="Loading slot…"
            />
          </div>
        )}
        {view === 'error' && (
          <div role="alert">
            <ErrorState
              size="sm"
              bordered={false}
              description="Could not load this slot."
              onRetry={() => feedUnitsQuery.refetch()}
            />
          </div>
        )}
        {view === 'loaded' && (
          <div className="flex flex-col gap-3">
            <div className="flex items-center gap-3 rounded-lg border px-3 py-2.5">
              <Swatch colorHex={variant?.colorHex} size="md" />
              <div className="flex min-w-0 flex-1 flex-col gap-1">
                <p className="text-[13px] font-medium break-words">
                  {filament
                    ? [filament.vendorName, filament.material]
                        .filter(Boolean)
                        .join(' ')
                    : 'Registered spool'}
                </p>
                {variant?.colorName && (
                  <p className="text-[11.5px] text-muted-foreground">
                    {variant.colorName}
                  </p>
                )}
                {capacity > 0 && remaining != null && (
                  <FillMeter
                    colorHex={variant?.colorHex}
                    percent={(remaining / capacity) * 100}
                  />
                )}
              </div>
              {remaining != null && (
                <p className="shrink-0 font-mono text-[11px] text-muted-foreground tabular-nums">
                  {formatGrams(remaining)}
                  {capacity > 0 && ` of ${formatGrams(capacity)}`}
                </p>
              )}
            </div>
            {feeding && (
              <p className="text-xs text-muted-foreground">
                Unloading clears the feeding slot.
              </p>
            )}
          </div>
        )}
        {view === 'empty' &&
          (spoolsQuery.isError ? (
            <div role="alert">
              <ErrorState
                size="sm"
                bordered={false}
                description="Could not load spools."
                onRetry={() => spoolsQuery.refetch()}
              />
            </div>
          ) : spoolsQuery.isLoading ? (
            <div role="status">
              <EmptyState
                size="sm"
                bordered={false}
                description="Loading spools…"
              />
            </div>
          ) : (
            <SpoolPicker
              spools={candidates}
              total={listing?.totalCount ?? candidates.length}
              catalog={catalog}
              query={query}
              onQueryChange={setQuery}
              refreshing={
                spoolsQuery.isPlaceholderData || query.trim() !== search
              }
              value={spoolId}
              onValueChange={setSpoolId}
            />
          ))}
      </DialogScrollableContent>
    </Dialog>
  );
}
