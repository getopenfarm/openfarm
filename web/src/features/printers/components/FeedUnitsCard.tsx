import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Plus } from 'lucide-react';
import { useState } from 'react';
import { toast } from 'sonner';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { Swatch } from '@/components/swatch/Swatch';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Spinner } from '@/components/ui/spinner';
import { useFilamentCatalog } from '@/features/filament/data/useFilamentCatalog';
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
import { feedSlotView, normalizeFilamentColor } from '../lib/feedSlotView';
import { selectReloaderChannel } from '../lib/reloaderApi';
import { AddFeedUnitDialog } from './AddFeedUnitDialog';

export interface FeedUnitsCardProps {
  printerId: string;
  amsUnits?: AmsUnitResponse[];
  isReachable?: boolean;
  externalSpoolTray?: AmsTrayResponse | null;
}

function FeedSlotRow({
  unit,
  slot,
  amsUnits,
  isReachable,
  externalSpoolTray,
  externalPortReplacedByReloader,
  unitPrinterId,
}: {
  unitPrinterId: string;
  unit: FeedUnitResponse;
  slot: FeedSlotResponse;
  amsUnits: AmsUnitResponse[];
  isReachable: boolean;
  externalSpoolTray?: AmsTrayResponse | null;
  externalPortReplacedByReloader?: boolean;
}) {
  const filaments = useFilamentCatalog();
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
    mutationFn: () => selectReloaderChannel(unit.id ?? '', slot.id ?? ''),
    onSuccess: () =>
      queryClient.invalidateQueries({
        queryKey: [`/api/v1/printers/${unitPrinterId}/feed-units`],
      }),
    onError: () => toast.error('Could not set feeding slot.'),
  });
  const isReloader = unit.kind === 'AutomaticFilamentReloader';
  const feeding = isReloader && slot.id === unit.feedingSlotId;
  const inactive = Boolean(slot.deactivatedAtUtc);
  const title = spool
    ? filament
      ? `${filament.vendorName} ${filament.material}`
      : 'Registered spool'
    : view.empty
      ? 'Empty'
      : (view.material ?? 'No registered spool');
  const detected = [view.material, view.subBrand].filter(Boolean).join(' · ');
  const differentDetection =
    spool &&
    (view.material !== filament?.materialType ||
      view.subBrand ||
      (view.color && view.color !== normalizeFilamentColor(variant?.colorHex)));

  return (
    <li
      aria-label={slot.displayLabel}
      className={cn(
        'flex flex-wrap items-center gap-3 border-b px-[18px] py-3 last:border-b-0',
        view.active && 'bg-accent/40',
        inactive && 'opacity-50',
      )}
    >
      <span className="w-12 shrink-0 font-mono text-[11px] text-muted-foreground">
        {slot.slotPosition != null
          ? `Slot ${slot.slotPosition + 1}`
          : slot.displayLabel}
      </span>
      <Swatch
        colorHex={
          spool ? normalizeFilamentColor(variant?.colorHex) : view.color
        }
        size="sm"
      />
      <div className="min-w-0 flex-1 basis-24">
        <p className="text-[13px] font-medium break-words">{title}</p>
        {variant && (
          <p className="text-[11.5px] text-muted-foreground">
            {variant.colorName}
          </p>
        )}
        {!spool && view.material && (
          <p className="text-[11.5px] text-muted-foreground">
            Detected · Unregistered{view.subBrand ? ` · ${view.subBrand}` : ''}
          </p>
        )}
        {spool && view.empty && (
          <p className="text-[11.5px] text-muted-foreground">
            Printer reports empty
          </p>
        )}
        {differentDetection && detected && (
          <p className="flex items-center gap-1.5 text-[11.5px] text-muted-foreground">
            {view.color && <Swatch colorHex={view.color} size="sm" />}
            Printer detected: {detected}
          </p>
        )}
        {!view.empty && !view.material && (
          <p className="text-[11.5px] text-muted-foreground">
            Detection unavailable
          </p>
        )}
        {isReloader && (
          <p className="text-[11.5px] text-muted-foreground">
            {feeding
              ? `Feeding${unit.feedingSelectionSource === 'Estimated' ? ' · Estimated' : ' · Operator selected'}`
              : spool
                ? 'Standby'
                : 'Unloaded'}
          </p>
        )}
        {feeding && unit.replacesFeedUnitId && view.material && (
          <p className="text-[11.5px] text-muted-foreground">
            Printer-port detection
          </p>
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
        {inactive && (
          <p className="text-[11.5px] text-muted-foreground">Inactive slot</p>
        )}
        {view.active && (
          <span className="inline-flex rounded bg-accent px-1.5 py-0.5 text-[11px] font-medium">
            Active
          </span>
        )}
      </div>
      <div className="flex flex-col items-end gap-1 text-right font-mono text-[11px] text-muted-foreground">
        {spool?.remainingWeightGrams != null && (
          <span>Recorded: {formatGrams(spool.remainingWeightGrams)}</span>
        )}
        {view.remainPercent != null && (
          <span
            className={cn(
              isReachable &&
                !inactive &&
                (view.remainPercent <= 15
                  ? 'text-status-offline'
                  : view.remainPercent <= 30
                    ? 'text-status-attention'
                    : ''),
            )}
          >
            Printer estimate: {view.remainPercent}%
          </span>
        )}
      </div>
    </li>
  );
}

function FeedUnitGroup({
  unit,
  printerId,
  amsUnits,
  isReachable,
  externalSpoolTray,
  externalPortReplacedByReloader,
}: {
  amsUnits: AmsUnitResponse[];
  isReachable: boolean;
  externalSpoolTray?: AmsTrayResponse | null;
  externalPortReplacedByReloader?: boolean;
  unit: FeedUnitResponse;
  printerId: string;
}) {
  const queryClient = useQueryClient();
  const [configureOpen, setConfigureOpen] = useState(false);
  const removeMutation = useOpenFarmApiEndpointsFeedUnitsRemoveFeedUnitEndpoint(
    {
      mutation: {
        onSuccess: () => {
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
          toast.error('Could not remove feed unit.');
        },
      },
    },
  );

  return (
    <div className="border-b last:border-b-0">
      <div className="flex items-center justify-between px-[18px] py-2">
        <p className="text-xs font-medium text-muted-foreground">
          {unit.displayLabel}
        </p>
        <div className="flex items-center gap-1">
          {unit.kind === 'AutomaticFilamentReloader' && (
            <Button
              variant="ghost"
              size="sm"
              className="h-6 px-2 text-xs"
              onClick={() => setConfigureOpen(true)}
            >
              Edit reloader
            </Button>
          )}
          {unit.id &&
          (unit.kind === 'AutomaticFilamentReloader' ||
            (unit.kind === 'ExternalSpool' && unit.supersededByFeedUnitId)) ? (
            <Button
              variant="ghost"
              size="sm"
              className="h-6 px-2 text-xs"
              disabled={removeMutation.isPending}
              onClick={() => removeMutation.mutate({ id: unit.id ?? '' })}
            >
              {removeMutation.isPending && <Spinner data-icon="inline-start" />}
              Remove
            </Button>
          ) : null}
        </div>
      </div>
      {unit.kind === 'AutomaticFilamentReloader' && !unit.feedingSlotId && (
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
            externalPortReplacedByReloader={externalPortReplacedByReloader}
            externalSpoolTray={externalSpoolTray}
            slot={slot}
            amsUnits={amsUnits}
            isReachable={isReachable}
          />
        ))}
      </ul>
      <AddFeedUnitDialog
        printerId={printerId}
        unit={unit}
        open={configureOpen}
        onOpenChange={setConfigureOpen}
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
        <Button
          variant="outline"
          size="icon"
          className="size-7"
          aria-label="Add feed unit"
          onClick={() => setAddOpen(true)}
        >
          <Plus data-icon="inline-start" className="" aria-hidden="true" />
        </Button>
      </CardHeader>
      <CardContent className="p-0">
        {!isReachable && (
          <p className="border-b px-[18px] py-2 text-xs text-muted-foreground">
            Printer offline · Detection is last known
          </p>
        )}
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
    </Card>
  );
}
