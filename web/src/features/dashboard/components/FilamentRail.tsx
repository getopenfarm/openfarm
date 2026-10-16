import { CircleHelp, Minus } from 'lucide-react';
import type { ReactNode } from 'react';
import { useFilamentCatalog } from '@/features/filament/data/useFilamentCatalog';
import type {
  ListFeedUnitsResponse,
  ListSpoolsResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsFeedUnitsListFeedUnitsEndpoint } from '@/lib/api/generated/printers/printers';
import { useOpenFarmApiEndpointsSpoolsListSpoolsEndpoint } from '@/lib/api/generated/spools/spools';
import { formatGrams } from '@/lib/formatUnits';
import { pluralize } from '@/lib/pluralize';
import { cn } from '@/lib/utils';
import { filamentIndicator } from '../lib/filamentIndicator';
import { buildFilamentRail, type FilamentMeter } from '../lib/filamentMeters';
import type { MonitoredPrinter } from '../lib/monitoring';
import { DashboardHint } from './DashboardHint';

function meterDescription(meter: FilamentMeter) {
  return `${meter.location}: ${meter.grams != null ? `${formatGrams(Math.round(meter.grams))} recorded` : meter.source === 'empty' ? 'Empty' : meter.source === 'printer' ? `${meter.level}% printer estimate` : 'Remaining quantity unknown'}`;
}
function Meter({
  meter,
  offline,
  className,
  equipmentDetails,
}: {
  meter: FilamentMeter;
  offline: boolean;
  className?: string;
  equipmentDetails: ReactNode;
}) {
  return (
    <DashboardHint
      label={meterDescription(meter)}
      className={cn(
        'relative min-h-0 min-w-0 rounded-sm bg-card focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring',
        className,
      )}
      details={
        <>
          <p className="font-medium">{meter.location}</p>
          <p>{meter.material}</p>
          {meter.grams != null && (
            <p>
              {formatGrams(Math.round(meter.grams))} recorded
              {meter.capacity != null
                ? ` / ${formatGrams(meter.capacity)} capacity`
                : ''}
            </p>
          )}
          <p className="text-muted-foreground">
            {meter.source === 'recorded'
              ? `${Math.round(meter.level ?? 0)}% · Recorded inventory`
              : meter.source === 'printer'
                ? `${meter.level}% · Printer estimate`
                : meter.source === 'empty'
                  ? 'Printer reports empty'
                  : 'Remaining quantity unknown'}
          </p>
          {meter.source !== 'printer' && meter.printerPercent != null && (
            <p className="text-muted-foreground">
              Printer estimate: {meter.printerPercent}%
            </p>
          )}
          {meter.active ? (
            <p>Active feed</p>
          ) : meter.selected ? (
            <p>
              Feeding · {meter.estimated ? 'Estimated' : 'Operator selected'}
            </p>
          ) : meter.standby ? (
            <p>Standby</p>
          ) : null}
          {meter.reportedEmpty && meter.source !== 'empty' && (
            <p>Printer reports empty</p>
          )}
          {equipmentDetails}
          {offline && (
            <p className="text-muted-foreground">
              Printer offline · Last-known readings
            </p>
          )}
        </>
      }
    >
      <span className="absolute inset-0 overflow-hidden rounded-sm bg-foreground/10">
        {meter.level != null ? (
          <>
            <meter
              className="sr-only"
              aria-label={`${meter.location} remaining`}
              min={0}
              max={100}
              value={meter.level}
              aria-valuetext={meterDescription(meter)}
            />
            <span aria-hidden="true" className="absolute inset-0">
              <span
                className="filament-consumed absolute inset-x-0 top-0"
                style={{ height: `${100 - meter.level}%` }}
              />
              <span
                className="absolute inset-x-0 bottom-0"
                style={{
                  height: `${meter.level}%`,
                  backgroundColor: meter.color ?? 'var(--muted-foreground)',
                }}
              />
              {meter.level === 0 && (
                <Minus className="absolute top-1/2 left-1/2 size-3 -translate-x-1/2 -translate-y-1/2 text-foreground" />
              )}
            </span>
          </>
        ) : meter.hasAssignedSpool && meter.color ? (
          <span
            aria-hidden="true"
            className="absolute inset-0"
            style={{ backgroundColor: meter.color }}
          />
        ) : (
          <span
            aria-hidden="true"
            className="filament-consumed absolute inset-0"
          >
            <CircleHelp className="absolute top-1/2 left-1/2 size-3.5 -translate-x-1/2 -translate-y-1/2 text-destructive" />
          </span>
        )}
      </span>
      {(meter.active || meter.selected) && (
        <span
          aria-hidden="true"
          className="pointer-events-none absolute left-1/2 h-[2px] w-3/5 -translate-x-1/2 rounded-full"
          style={{
            ...filamentIndicator(
              (meter.level != null && meter.level > 0) ||
                (meter.level == null && meter.hasAssignedSpool)
                ? meter.color
                : undefined,
            ),
            // Place the marker 4px below the fill edge; keep it inside at empty.
            top: `min(calc(${100 - (meter.level ?? 100)}% + 4px), calc(100% - 2px))`,
          }}
        />
      )}
    </DashboardHint>
  );
}

export function FilamentRail({ printer }: { printer: MonitoredPrinter }) {
  const feedQuery = useOpenFarmApiEndpointsFeedUnitsListFeedUnitsEndpoint(
    printer.id ?? '',
    undefined,
    { query: { enabled: Boolean(printer.id) } },
  );
  const spoolQuery = useOpenFarmApiEndpointsSpoolsListSpoolsEndpoint();
  const catalog = useFilamentCatalog();
  const units =
    (feedQuery.data?.data as ListFeedUnitsResponse | undefined)?.feedUnits ??
    [];
  const spools =
    (spoolQuery.data?.data as ListSpoolsResponse | undefined)?.spools ?? [];
  const rail = buildFilamentRail(printer, units, spools, catalog);
  const unavailable = feedQuery.isError || spoolQuery.isError;
  return (
    <section
      aria-label={`${printer.name} filament remaining`}
      className="flex w-12 shrink-0 flex-col gap-1"
    >
      <div className="grid min-h-0 flex-1 grid-cols-2 grid-rows-2 gap-1">
        {rail.meters.length ? (
          rail.meters.map((meter) => (
            <Meter
              key={meter.key}
              meter={meter}
              offline={!printer.isReachable}
              equipmentDetails={
                <>
                  {(rail.additionalUnits.length > 0 ||
                    rail.additionalSlots > 0) && (
                    <p className="text-muted-foreground">
                      Also installed:{' '}
                      {[
                        ...rail.additionalUnits,
                        rail.additionalSlots
                          ? `${pluralize(rail.additionalSlots, 'additional slot')}`
                          : '',
                      ]
                        .filter(Boolean)
                        .join(', ')}
                    </p>
                  )}
                  {unavailable && (
                    <p>Inventory unavailable · Values may be incomplete</p>
                  )}
                </>
              }
              className={
                rail.kind === 'ams'
                  ? undefined
                  : rail.kind === 'external' || rail.meters.length === 1
                    ? 'col-span-2 row-span-2'
                    : 'row-span-2'
              }
            />
          ))
        ) : (
          <DashboardHint
            label="Filament remaining unknown"
            className="filament-consumed col-span-2 row-span-2 flex items-center justify-center rounded-sm text-destructive"
            details={
              <p>
                {feedQuery.isLoading
                  ? 'Loading filament…'
                  : feedQuery.isError
                    ? 'Filament inventory unavailable'
                    : 'No feed equipment reported'}
              </p>
            }
          >
            <CircleHelp className="size-3.5" aria-hidden="true" />
          </DashboardHint>
        )}
      </div>
    </section>
  );
}
