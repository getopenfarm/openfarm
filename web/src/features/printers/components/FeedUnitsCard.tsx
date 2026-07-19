import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { toast } from 'sonner';
import { Swatch } from '@/components/swatch/Swatch';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { useFilamentCatalog } from '@/features/filament/data/useFilamentCatalog';
import type {
  CurrentSpoolResponse,
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
import { AddFeedUnitDialog } from './AddFeedUnitDialog';

const REMOVABLE_KINDS = new Set(['AutomaticFilamentReloader', 'ExternalSpool']);

export interface FeedUnitsCardProps {
  printerId: string;
}

function CurrentSpoolSummary({
  currentSpool,
}: {
  currentSpool: CurrentSpoolResponse;
}) {
  const filaments = useFilamentCatalog();
  const variant = filaments
    .flatMap((f) =>
      (f.variants ?? []).map((v) => ({ filament: f, variant: v })),
    )
    .find((entry) => entry.variant.id === currentSpool.filamentVariantId);

  return (
    <>
      <Swatch colorHex={variant?.variant.colorHex} size="sm" />
      <div className="min-w-0 flex-1">
        <p className="truncate text-[13px] font-medium">
          {variant
            ? `${variant.filament.vendorName} ${variant.filament.material}`
            : 'Loaded'}
        </p>
        {variant && (
          <p className="truncate text-[11.5px] text-muted-foreground">
            {variant.variant.colorName}
          </p>
        )}
      </div>
      <span className="font-mono text-xs text-muted-foreground">
        {currentSpool.remainingWeightGrams} g
      </span>
    </>
  );
}

function FeedSlotRow({ slot }: { slot: FeedSlotResponse }) {
  const deactivated = Boolean(slot.deactivatedAtUtc);

  return (
    <li
      className={cn(
        'flex items-center gap-3 border-b px-[18px] py-3 last:border-b-0',
        deactivated && 'opacity-50',
      )}
    >
      <span className="w-16 shrink-0 font-mono text-[11px] text-muted-foreground">
        {slot.displayLabel}
      </span>
      {slot.currentSpool ? (
        <CurrentSpoolSummary currentSpool={slot.currentSpool} />
      ) : slot.detectedMaterialType ? (
        <>
          <Swatch
            colorHex={
              slot.detectedColorHex ? `#${slot.detectedColorHex}` : undefined
            }
            size="sm"
          />
          <div className="min-w-0 flex-1">
            <p className="truncate text-[13px] font-medium">
              {slot.detectedMaterialType}
            </p>
            <p className="truncate text-[11.5px] text-muted-foreground">
              Detected, not registered as a spool
            </p>
          </div>
        </>
      ) : (
        <>
          <Swatch className="bg-transparent" size="sm" />
          <p className="flex-1 truncate text-[13px] text-muted-foreground">
            {deactivated ? 'Deactivated' : 'Empty'}
          </p>
        </>
      )}
    </li>
  );
}

function FeedUnitGroup({
  unit,
  printerId,
}: {
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
          <FeedSlotRow key={slot.id} slot={slot} />
        ))}
      </ul>
    </div>
  );
}

export function FeedUnitsCard({ printerId }: FeedUnitsCardProps) {
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
      <CardHeader className="flex-row items-center justify-between border-b px-[18px] py-3.5">
        <CardTitle className="text-sm font-semibold">Feed Units</CardTitle>
        <Button
          variant="outline"
          size="sm"
          className="h-7"
          onClick={() => setAddOpen(true)}
        >
          Add a Feed Unit
        </Button>
      </CardHeader>
      <CardContent className="p-0">
        {feedUnits.length === 0 ? (
          <p className="px-[18px] py-7 text-center text-sm text-muted-foreground">
            No Feed Units on this printer
          </p>
        ) : (
          <div className="flex flex-col">
            {feedUnits.map((unit) => (
              <FeedUnitGroup key={unit.id} unit={unit} printerId={printerId} />
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
