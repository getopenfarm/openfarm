import { useQueryClient } from '@tanstack/react-query';
import { Plus } from 'lucide-react';
import { useState } from 'react';
import { toast } from 'sonner';
import { Swatch } from '@/components/swatch/Swatch';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { useFilamentCatalog } from '@/features/filament/data/useFilamentCatalog';
import type {
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
import { cn } from '@/lib/utils';
import { feedSlotView, normalizeFilamentColor } from '../lib/feedSlotView';
import { AddFeedUnitDialog } from './AddFeedUnitDialog';

const REMOVABLE_KINDS = new Set(['AutomaticFilamentReloader', 'ExternalSpool']);

export interface FeedUnitsCardProps {
  printerId: string;
  amsUnits?: AmsUnitResponse[];
  isReachable?: boolean;
}

function FeedSlotRow({
  unit,
  slot,
  amsUnits,
  isReachable,
}: {
  unit: FeedUnitResponse;
  slot: FeedSlotResponse;
  amsUnits: AmsUnitResponse[];
  isReachable: boolean;
}) {
  const filaments = useFilamentCatalog();
  const spool = slot.currentSpool;
  const filament = filaments.find((f) =>
    (f.variants ?? []).some((v) => v.id === spool?.filamentVariantId),
  );
  const variant = filament?.variants?.find(
    (v) => v.id === spool?.filamentVariantId,
  );
  const view = feedSlotView(unit, slot, amsUnits, isReachable);
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
          <span>Tracked: {spool.remainingWeightGrams} g</span>
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
}: {
  amsUnits: AmsUnitResponse[];
  isReachable: boolean;
  unit: FeedUnitResponse;
  printerId: string;
}) {
  const queryClient = useQueryClient();
  const removeMutation = useOpenFarmApiEndpointsFeedUnitsRemoveFeedUnitEndpoint(
    {
      mutation: {
        onSuccess: () => {
          toast.success('Feed Unit removed.');
          queryClient.invalidateQueries({
            queryKey:
              getOpenFarmApiEndpointsFeedUnitsListFeedUnitsEndpointQueryKey(
                printerId,
              ),
          });
        },
        onError: () => {
          toast.error('Could not remove that Feed Unit.');
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
        {unit.kind && REMOVABLE_KINDS.has(unit.kind) && unit.id ? (
          <Button
            variant="ghost"
            size="sm"
            className="h-6 px-2 text-xs"
            disabled={removeMutation.isPending}
            onClick={() => removeMutation.mutate({ id: unit.id ?? '' })}
          >
            Remove
          </Button>
        ) : null}
      </div>
      <ul>
        {(unit.feedSlots ?? []).map((slot) => (
          <FeedSlotRow
            key={slot.id}
            unit={unit}
            slot={slot}
            amsUnits={amsUnits}
            isReachable={isReachable}
          />
        ))}
      </ul>
    </div>
  );
}

export function FeedUnitsCard({
  printerId,
  amsUnits = [],
  isReachable = false,
}: FeedUnitsCardProps) {
  const [addOpen, setAddOpen] = useState(false);
  const feedUnitsQuery = useOpenFarmApiEndpointsFeedUnitsListFeedUnitsEndpoint(
    printerId,
    { query: { enabled: Boolean(printerId) } },
  );
  const feedUnits =
    (feedUnitsQuery.data?.data as ListFeedUnitsResponse | undefined)
      ?.feedUnits ?? [];

  return (
    <Card className="gap-0 py-0">
      <CardHeader className="flex items-center justify-between border-b px-[18px] py-3.5">
        <CardTitle className="text-sm font-semibold">Feed Units</CardTitle>
        <Button
          variant="outline"
          size="icon"
          className="size-7"
          aria-label="Add a Feed Unit"
          title="Add a Feed Unit"
          onClick={() => setAddOpen(true)}
        >
          <Plus className="size-4" aria-hidden="true" />
        </Button>
      </CardHeader>
      <CardContent className="p-0">
        {!isReachable && (
          <p className="border-b px-[18px] py-2 text-xs text-muted-foreground">
            Printer offline · Detection is last known
          </p>
        )}
        {feedUnitsQuery.isError && (
          <div role="alert" className="px-[18px] py-3 text-sm">
            Could not load Feed Units.{' '}
            <Button
              variant="ghost"
              size="sm"
              onClick={() => feedUnitsQuery.refetch()}
            >
              Retry
            </Button>
          </div>
        )}
        {feedUnitsQuery.isLoading ? (
          <p
            role="status"
            className="px-[18px] py-7 text-sm text-muted-foreground"
          >
            Loading Feed Units…
          </p>
        ) : !feedUnitsQuery.data ? null : feedUnits.length === 0 ? (
          <p className="px-[18px] py-7 text-center text-sm text-muted-foreground">
            No Feed Units on this printer
          </p>
        ) : (
          <div className="flex flex-col">
            {feedUnits.map((unit) => (
              <FeedUnitGroup
                key={unit.id}
                unit={unit}
                printerId={printerId}
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
