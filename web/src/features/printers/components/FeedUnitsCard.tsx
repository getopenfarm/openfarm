import { Swatch } from '@/components/swatch/Swatch';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { useFilamentCatalog } from '@/features/filament/data/useFilamentCatalog';
import type {
  CurrentSpoolResponse,
  FeedSlotResponse,
  ListFeedUnitsResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsFeedUnitsListFeedUnitsEndpoint } from '@/lib/api/generated/printers/printers';
import { cn } from '@/lib/utils';

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

export function FeedUnitsCard({ printerId }: FeedUnitsCardProps) {
  const feedUnitsQuery = useOpenFarmApiEndpointsFeedUnitsListFeedUnitsEndpoint(
    printerId,
    { query: { enabled: Boolean(printerId) } },
  );
  const feedUnits =
    (feedUnitsQuery.data?.data as ListFeedUnitsResponse | undefined)
      ?.feedUnits ?? [];

  return (
    <Card className="gap-0 py-0">
      <CardHeader className="border-b px-[18px] py-3.5">
        <CardTitle className="text-sm font-semibold">Feed Units</CardTitle>
      </CardHeader>
      <CardContent className="p-0">
        {feedUnits.length === 0 ? (
          <p className="px-[18px] py-7 text-center text-sm text-muted-foreground">
            No Feed Units on this printer
          </p>
        ) : (
          <div className="flex flex-col">
            {feedUnits.map((unit) => (
              <div key={unit.id} className="border-b last:border-b-0">
                <p className="px-[18px] py-2 text-xs font-medium text-muted-foreground">
                  {unit.displayLabel}
                </p>
                <ul>
                  {(unit.feedSlots ?? []).map((slot) => (
                    <FeedSlotRow key={slot.id} slot={slot} />
                  ))}
                </ul>
              </div>
            ))}
          </div>
        )}
      </CardContent>
    </Card>
  );
}
