import { useMutation, useQueryClient } from '@tanstack/react-query';
import { ChevronRight, Plus } from 'lucide-react';
import { useRef, useState } from 'react';
import { toast } from 'sonner';
import { ConfirmDialog } from '@/components/confirm-dialog/ConfirmDialog';
import { DataTableRowActions } from '@/components/data-table/DataTableRowActions';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { Swatch } from '@/components/swatch/Swatch';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import {
  DropdownMenuGroup,
  DropdownMenuItem,
} from '@/components/ui/dropdown-menu';
import { Spinner } from '@/components/ui/spinner';
import { useFilamentCatalog } from '@/features/filament/data/useFilamentCatalog';
import { FillMeter } from '@/features/spools/components/FillMeter';
import { ManageSlotDialog } from '@/features/spools/components/ManageSlotDialog';
import type {
  AmsTrayResponse,
  AmsUnitResponse,
  FeedSlotResponse,
  FeedUnitResponse,
  ListFeedUnitsResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsFeedUnitsRemoveFeedUnitEndpoint } from '@/lib/api/generated/feed-units/feed-units';
import {
  getOpenFarmApiEndpointsFeedUnitsListFeedUnitsEndpointQueryKey,
  useOpenFarmApiEndpointsFeedUnitsListFeedUnitsEndpoint,
} from '@/lib/api/generated/printers/printers';
import { formatGrams } from '@/lib/formatUnits';
import { cn } from '@/lib/utils';
import {
  colorsDiffer,
  detectedName,
  feedSlotView,
  normalizeFilamentColor,
} from '../lib/feedSlotView';
import { selectReloaderSlot } from '../lib/reloaderApi';
import { AddFeedUnitDialog } from './AddFeedUnitDialog';

export interface FeedUnitsCardProps {
  printerId: string;
  amsUnits?: AmsUnitResponse[];
  isReachable?: boolean;
  externalSpoolTray?: AmsTrayResponse | null;
}

type Filaments = ReturnType<typeof useFilamentCatalog>;

/** One slot = what is loaded, how much is left, one status tag. The row opens the slot dialog. */
function FeedSlotRow({
  unit,
  slot,
  filaments,
  amsUnits,
  isReachable,
  externalSpoolTray,
  externalPortReplacedByReloader,
  unitPrinterId,
  onManage,
}: {
  unitPrinterId: string;
  unit: FeedUnitResponse;
  slot: FeedSlotResponse;
  filaments: Filaments;
  amsUnits: AmsUnitResponse[];
  isReachable: boolean;
  externalSpoolTray?: AmsTrayResponse | null;
  externalPortReplacedByReloader?: boolean;
  onManage: () => void;
}) {
  const spool = slot.currentSpool;
  const filament = filaments.find((f) =>
    (f.variants ?? []).some((v) => v.id === spool?.filamentVariantId),
  );
  const variant = filament?.variants?.find(
    (v) => v.id === spool?.filamentVariantId,
  );
  const view = feedSlotView(
    unit,
    slot,
    amsUnits,
    isReachable,
    externalSpoolTray,
    externalPortReplacedByReloader,
  );
  const queryClient = useQueryClient();
  const slotMutation = useMutation({
    mutationFn: () => selectReloaderSlot(unit.id ?? '', slot.id ?? ''),
    onSuccess: () =>
      queryClient.invalidateQueries({
        queryKey: [`/api/v1/printers/${unitPrinterId}/feed-units`],
      }),
    onError: () => toast.error('Could not set feeding slot.'),
  });
  const isReloader = unit.kind === 'AutomaticFilamentReloader';
  const feeding = isReloader && slot.id === unit.feedingSlotId;
  const inactive = Boolean(slot.deactivatedAtUtc);
  const canManage = !inactive && Boolean(slot.id);
  const detected = detectedName(view.material, view.subBrand);
  const title = spool
    ? filament
      ? `${filament.vendorName} ${filament.material}`
      : 'Registered spool'
    : view.empty
      ? 'Empty'
      : detected || 'No spool';
  // Flag only what differs from the registered spool; a sub-brand that is already in its name is not a difference.
  const registeredName =
    `${filament?.vendorName} ${filament?.material}`.toLowerCase();
  const materialDiffers =
    Boolean(spool && view.material) &&
    view.material?.toLowerCase() !== filament?.materialType?.toLowerCase();
  const subBrandDiffers =
    Boolean(spool && view.subBrand) &&
    !registeredName.includes(view.subBrand?.toLowerCase() ?? '');
  const colorDiffers =
    Boolean(spool && view.color) &&
    colorsDiffer(view.color, normalizeFilamentColor(variant?.colorHex));
  // One status tag at most: inactive > tray empty > mismatch.
  const tag = inactive
    ? 'Inactive'
    : spool && view.empty
      ? 'Tray empty'
      : materialDiffers || subBrandDiffers
        ? `Detected: ${detected}`
        : colorDiffers
          ? 'Color differs'
          : null;
  const capacity = spool?.spoolWeightGrams ?? 0;
  const lowPercent =
    view.remainPercent != null && isReachable && !inactive
      ? view.remainPercent <= 15
        ? 'text-status-offline'
        : view.remainPercent <= 30
          ? 'text-status-attention'
          : ''
      : '';

  const content = (
    <>
      <span className="w-4 shrink-0 text-center text-xs text-muted-foreground tabular-nums">
        <span className="sr-only">{`${unit.displayLabel} · ${slot.displayLabel}`}</span>
        <span aria-hidden="true">
          {slot.slotPosition != null
            ? slot.slotPosition + 1
            : slot.displayLabel}
        </span>
      </span>
      <Swatch
        colorHex={
          spool ? normalizeFilamentColor(variant?.colorHex) : view.color
        }
        size="sm"
      />
      <span className="min-w-0 flex-1">
        <span className="flex flex-wrap items-center gap-x-2 text-[13px]">
          <span className="font-medium break-words">{title}</span>
          {variant && (
            <span className="text-muted-foreground">{variant.colorName}</span>
          )}
          {view.active ? (
            <Badge kind="green">Active</Badge>
          ) : (
            feeding && (
              <Badge
                kind="zinc"
                title={
                  unit.feedingSelectionSource === 'Estimated'
                    ? 'Estimated'
                    : undefined
                }
              >
                Feeding
              </Badge>
            )
          )}
        </span>
        {tag && (
          <span className="block text-[11.5px] text-muted-foreground">
            {tag}
          </span>
        )}
      </span>
      <span className="flex shrink-0 items-center gap-2 text-xs text-muted-foreground tabular-nums">
        {spool?.remainingWeightGrams != null ? (
          <>
            {capacity > 0 && (
              <FillMeter
                className="w-14"
                percent={(spool.remainingWeightGrams / capacity) * 100}
              />
            )}
            <span>{formatGrams(spool.remainingWeightGrams)}</span>
          </>
        ) : !spool && view.material ? (
          <>
            <span>Unregistered</span>
            {view.remainPercent != null && (
              <span className={lowPercent} title="Printer estimate">
                {view.remainPercent}%
              </span>
            )}
          </>
        ) : !spool && canManage ? (
          <span>Load spool</span>
        ) : null}
      </span>
      {canManage && (
        <ChevronRight
          className="size-4 shrink-0 text-muted-foreground"
          aria-hidden="true"
        />
      )}
    </>
  );

  return (
    <li
      aria-label={slot.displayLabel}
      className={cn(
        'relative flex items-center gap-2 border-b px-[18px] py-2.5 last:border-b-0',
        inactive && 'opacity-50',
      )}
    >
      {canManage ? (
        <button
          type="button"
          className="flex min-w-0 flex-1 items-center gap-3 rounded-sm text-left outline-none focus-visible:ring-[3px] focus-visible:ring-ring/50"
          onClick={onManage}
        >
          {content}
        </button>
      ) : (
        <div className="flex min-w-0 flex-1 items-center gap-3">{content}</div>
      )}
      {isReloader && !feeding && !inactive && (
        <Button
          size="sm"
          variant="ghost"
          disabled={slotMutation.isPending}
          onClick={() => slotMutation.mutate()}
        >
          {slotMutation.isPending && <Spinner data-icon="inline-start" />}
          Set feeding slot
        </Button>
      )}
    </li>
  );
}

function FeedUnitGroup({
  unit,
  printerId,
  filaments,
  amsUnits,
  isReachable,
  externalSpoolTray,
  externalPortReplacedByReloader,
  onManageSlot,
}: {
  amsUnits: AmsUnitResponse[];
  filaments: Filaments;
  isReachable: boolean;
  externalSpoolTray?: AmsTrayResponse | null;
  externalPortReplacedByReloader?: boolean;
  unit: FeedUnitResponse;
  printerId: string;
  onManageSlot: (slot: FeedSlotResponse) => void;
}) {
  const queryClient = useQueryClient();
  const menuTriggerRef = useRef<HTMLElement | null>(null);
  const [configureOpen, setConfigureOpen] = useState(false);
  const [removeOpen, setRemoveOpen] = useState(false);
  const removeMutation = useOpenFarmApiEndpointsFeedUnitsRemoveFeedUnitEndpoint(
    {
      mutation: {
        onSuccess: () => {
          setRemoveOpen(false);
          toast.success(
            'Feed unit removed. Loaded spools returned to inventory.',
          );
          queryClient.invalidateQueries({
            predicate: (query) =>
              typeof query.queryKey[0] === 'string' &&
              query.queryKey[0].startsWith('/api/v1/spools'),
          });
          queryClient.invalidateQueries({
            queryKey:
              getOpenFarmApiEndpointsFeedUnitsListFeedUnitsEndpointQueryKey(
                printerId,
              ),
          });
        },
        onError: () => {
          setRemoveOpen(false);
          toast.error('Could not remove feed unit.');
        },
      },
    },
  );
  const isReloader = unit.kind === 'AutomaticFilamentReloader';
  const canRemove =
    Boolean(unit.id) &&
    (isReloader ||
      (unit.kind === 'ExternalSpool' && Boolean(unit.supersededByFeedUnitId)));

  return (
    <div className="border-b last:border-b-0">
      <div className="flex min-h-9 items-center justify-between px-[18px] py-1">
        <p className="text-xs font-medium text-muted-foreground">
          {unit.displayLabel}
        </p>
        {(isReloader || canRemove) && (
          <DataTableRowActions
            label={unit.displayLabel ?? 'feed unit'}
            onOpen={(trigger) => {
              menuTriggerRef.current = trigger;
            }}
          >
            <DropdownMenuGroup>
              {isReloader && (
                <DropdownMenuItem onSelect={() => setConfigureOpen(true)}>
                  Edit reloader
                </DropdownMenuItem>
              )}
              {canRemove && (
                <DropdownMenuItem
                  variant="destructive"
                  onSelect={() => setRemoveOpen(true)}
                >
                  Remove
                </DropdownMenuItem>
              )}
            </DropdownMenuGroup>
          </DataTableRowActions>
        )}
      </div>
      {isReloader && !unit.feedingSlotId && (
        <p className="px-[18px] pb-2 text-xs text-muted-foreground">
          Feeding slot not set
        </p>
      )}
      <ul>
        {(unit.feedSlots ?? []).map((slot) => (
          <FeedSlotRow
            key={slot.id}
            unit={unit}
            unitPrinterId={printerId}
            filaments={filaments}
            externalPortReplacedByReloader={externalPortReplacedByReloader}
            externalSpoolTray={externalSpoolTray}
            slot={slot}
            amsUnits={amsUnits}
            isReachable={isReachable}
            onManage={() => onManageSlot(slot)}
          />
        ))}
      </ul>
      <AddFeedUnitDialog
        printerId={printerId}
        unit={unit}
        open={configureOpen}
        onOpenChange={setConfigureOpen}
      />
      <ConfirmDialog
        open={removeOpen}
        onOpenChange={setRemoveOpen}
        title="Remove feed unit"
        description="Loaded spools return to inventory."
        confirmLabel="Remove"
        destructive
        closeOnConfirm={false}
        isPending={removeMutation.isPending}
        returnFocusRef={menuTriggerRef}
        onConfirm={() => removeMutation.mutate({ id: unit.id ?? '' })}
      />
    </div>
  );
}

export function FeedUnitsCard({
  printerId,
  amsUnits = [],
  isReachable = false,
  externalSpoolTray,
}: FeedUnitsCardProps) {
  const [addOpen, setAddOpen] = useState(false);
  const [managing, setManaging] = useState<{
    slotId: string;
    label: string;
  } | null>(null);
  const filaments = useFilamentCatalog();
  const feedUnitsQuery = useOpenFarmApiEndpointsFeedUnitsListFeedUnitsEndpoint(
    printerId,
    undefined,
    { query: { enabled: Boolean(printerId) } },
  );
  const feedUnits =
    (feedUnitsQuery.data?.data as ListFeedUnitsResponse | undefined)
      ?.feedUnits ?? [];

  return (
    <Card className="gap-0 py-0">
      <CardHeader className="flex items-center justify-between border-b px-[18px] py-3.5">
        <CardTitle className="text-sm font-semibold">Feed units</CardTitle>
        <div className="flex items-center gap-3">
          {!isReachable && (
            <span className="text-xs text-muted-foreground">
              Offline · last known
            </span>
          )}
          <Button
            variant="outline"
            size="icon"
            className="size-7"
            aria-label="Add feed unit"
            onClick={() => setAddOpen(true)}
          >
            <Plus data-icon="inline-start" className="" aria-hidden="true" />
          </Button>
        </div>
      </CardHeader>
      <CardContent className="p-0">
        {feedUnitsQuery.isError && (
          <div role="alert">
            <ErrorState
              size="sm"
              bordered={false}
              description="Could not load feed units."
              onRetry={() => feedUnitsQuery.refetch()}
            />
          </div>
        )}
        {feedUnitsQuery.isLoading ? (
          <div role="status">
            <EmptyState
              size="sm"
              bordered={false}
              description="Loading feed units…"
            />
          </div>
        ) : !feedUnitsQuery.data ? null : feedUnits.length === 0 ? (
          <EmptyState size="sm" bordered={false} title="No feed units" />
        ) : (
          <div className="flex flex-col">
            {feedUnits.map((unit) => (
              <FeedUnitGroup
                key={unit.id}
                unit={unit}
                printerId={printerId}
                filaments={filaments}
                externalPortReplacedByReloader={
                  unit.kind === 'ExternalSpool' &&
                  feedUnits.some(
                    (other) =>
                      other.kind === 'AutomaticFilamentReloader' &&
                      !other.archivedAtUtc &&
                      other.replacesFeedUnitId === unit.id,
                  )
                }
                externalSpoolTray={externalSpoolTray}
                amsUnits={amsUnits}
                isReachable={isReachable}
                onManageSlot={(slot) =>
                  slot.id &&
                  setManaging({
                    slotId: slot.id,
                    label: `${unit.displayLabel} · ${slot.displayLabel}`,
                  })
                }
              />
            ))}
          </div>
        )}
      </CardContent>
      <AddFeedUnitDialog
        printerId={printerId}
        open={addOpen}
        onOpenChange={setAddOpen}
      />
      {managing && (
        <ManageSlotDialog
          printerId={printerId}
          feedSlotId={managing.slotId}
          slotLabel={managing.label}
          open
          onOpenChange={(open) => !open && setManaging(null)}
        />
      )}
    </Card>
  );
}
