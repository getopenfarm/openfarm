import { CircleHelp, Minus } from 'lucide-react';
import { type ReactNode, useEffect, useRef, useState } from 'react';
import {
  Popover,
  PopoverAnchor,
  PopoverContent,
} from '@/components/ui/popover';
import { useFilamentCatalog } from '@/features/filament/data/useFilamentCatalog';
import { ManageSlotDialog } from '@/features/spools/components/ManageSlotDialog';
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
function MeterBar({
  meter,
  active,
  className,
  onHover,
  onLeave,
  onSelect,
}: {
  meter: FilamentMeter;
  active: boolean;
  className?: string;
  onHover: () => void;
  onLeave: () => void;
  onSelect: () => void;
}) {
  return (
    <button
      type="button"
      aria-label={meterDescription(meter)}
      aria-haspopup="dialog"
      aria-expanded={active}
      className={cn(
        'relative min-h-0 min-w-0 rounded-sm bg-card focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring',
        className,
      )}
      onPointerEnter={(event) => {
        if (event.pointerType !== 'touch') onHover();
      }}
      onPointerLeave={onLeave}
      onFocus={onHover}
      onBlur={onLeave}
      onClick={onSelect}
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
    </button>
  );
}

function MeterDetails({
  meter,
  offline,
  equipmentDetails,
}: {
  meter: FilamentMeter;
  offline: boolean;
  equipmentDetails: ReactNode;
}) {
  return (
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
        <p>Feeding · {meter.estimated ? 'Estimated' : 'Operator selected'}</p>
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
      {meter.slotId && (
        <p className="text-muted-foreground">Click to manage slot.</p>
      )}
    </>
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

  // One hover popover for the whole rail, anchored to the rail so it opens beside both columns
  // instead of over the neighbouring bar. Clicking a bar opens the slot dialog.
  const [hovered, setHovered] = useState<string | null>(null);
  const [manageTarget, setManageTarget] = useState<FilamentMeter | null>(null);
  const [manageOpen, setManageOpen] = useState(false);
  const closing = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const railRef = useRef<HTMLElement>(null);
  useEffect(() => () => clearTimeout(closing.current), []);
  const active = rail.meters.find((meter) => meter.key === hovered);

  function hover(key: string) {
    clearTimeout(closing.current);
    setHovered(key);
  }
  function leave() {
    clearTimeout(closing.current);
    closing.current = setTimeout(() => setHovered(null), 150);
  }
  function select(meter: FilamentMeter) {
    if (!meter.slotId) return hover(meter.key); // nothing to manage: touch gets the readout
    clearTimeout(closing.current);
    setHovered(null);
    setManageTarget(meter);
    setManageOpen(true);
  }

  return (
    <Popover
      open={Boolean(active)}
      onOpenChange={(open) => !open && setHovered(null)}
    >
      <PopoverAnchor asChild>
        <section
          ref={railRef}
          aria-label={`${printer.name} filament remaining`}
          className="flex w-12 shrink-0 flex-col gap-1"
        >
          <div className="grid min-h-0 flex-1 grid-cols-2 grid-rows-2 gap-1">
            {rail.meters.length ? (
              rail.meters.map((meter) => (
                <MeterBar
                  key={meter.key}
                  meter={meter}
                  active={meter.key === hovered}
                  onHover={() => hover(meter.key)}
                  onLeave={leave}
                  onSelect={() => select(meter)}
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
      </PopoverAnchor>
      {active && (
        <PopoverContent
          aria-label={meterDescription(active)}
          className="w-64 gap-1 text-xs"
          side="left"
          onOpenAutoFocus={(event) => event.preventDefault()}
          onCloseAutoFocus={(event) => event.preventDefault()}
          // The bars manage their own hover state; only interaction beyond the rail dismisses.
          onInteractOutside={(event) => {
            if (
              event.target instanceof Node &&
              railRef.current?.contains(event.target)
            )
              event.preventDefault();
          }}
          onPointerEnter={() => clearTimeout(closing.current)}
          onPointerLeave={leave}
        >
          <MeterDetails
            meter={active}
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
          />
        </PopoverContent>
      )}
      {manageTarget?.slotId && (
        <ManageSlotDialog
          printerId={printer.id ?? ''}
          feedSlotId={manageTarget.slotId}
          slotLabel={manageTarget.location}
          open={manageOpen}
          onOpenChange={setManageOpen}
        />
      )}
    </Popover>
  );
}
