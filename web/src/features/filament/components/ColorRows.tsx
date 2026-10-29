import { ChevronDownIcon, PlusIcon } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router';
import { StatusBadge } from '@/components/status-badge/StatusBadge';
import { Swatch } from '@/components/swatch/Swatch';
import { Button } from '@/components/ui/button';
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from '@/components/ui/collapsible';
import { filamentIndicator } from '@/features/dashboard/lib/filamentIndicator';
import { useCurrency } from '@/features/settings/generalSettings';
import { FillMeter } from '@/features/spools/components/FillMeter';
import { groupShelf } from '@/features/spools/lib/groupShelf';
import type {
  DailyUsageResponse,
  FilamentResponse,
  FilamentShortageResponse,
  FilamentVariantResponse,
  SpoolResponse,
  VariantStockResponse,
} from '@/lib/api/generated/endpoints.schemas';
import {
  formatCost,
  formatDays,
  formatGrams,
  formatWeight,
} from '@/lib/formatUnits';
import { pluralize } from '@/lib/pluralize';
import { cn } from '@/lib/utils';
import { addSpoolUrl } from '../lib/addSpoolUrl';

/** The runway's scale: about one restock cycle. */
const HORIZON_DAYS = 60;

const COLUMNS =
  'md:grid-cols-[minmax(9rem,1.1fr)_minmax(12rem,1.7fr)_6.5rem_minmax(10rem,1.4fr)_4.5rem]';

interface ColorRowData {
  variant: FilamentVariantResponse;
  stock?: VariantStockResponse;
  /** Grams queued work needs beyond what is on hand; 0 when covered. */
  shortGrams: number;
  /** Grams drawn per day, oldest first; empty without use. */
  drawn: number[];
  waste: number[];
  /** The color's unfinished spools. */
  spools: SpoolResponse[];
}

const sum = (values: number[]) => values.reduce((total, v) => total + v, 0);

/** A filament close to the card's color needs a stronger outline: a light one (white, cream) on the light theme, a dark one on the dark theme. */
const isLight = (colorHex?: string) =>
  filamentIndicator(colorHex).backgroundColor === '#171717';

/**
 * One row per color, most used first: how many days its stock lasts at the last 30 days' pace, what it holds, and how it was used
 * day by day. The color's own filament color draws its runway and its bars; expanding a row shows its spools.
 */
export function ColorRows({
  filament,
  stock,
  shortages,
  usage,
  spools,
}: {
  filament: FilamentResponse;
  stock: VariantStockResponse[];
  shortages: FilamentShortageResponse[];
  usage?: DailyUsageResponse;
  /** The filament's spools in printers and on the shelf. */
  spools: SpoolResponse[];
}) {
  const days = usage?.days ?? [];
  const rows: ColorRowData[] = (filament.variants ?? [])
    .map((variant) => {
      const short = shortages.find((s) => s.filamentVariantId === variant.id);
      const daily = usage?.colors?.find(
        (c) => c.filamentVariantId === variant.id,
      );
      return {
        variant,
        stock: stock.find((row) => row.filamentVariantId === variant.id),
        shortGrams: short
          ? Math.max(0, (short.queuedGrams ?? 0) - (short.onHandGrams ?? 0))
          : 0,
        drawn: daily?.drawnGrams ?? [],
        waste: daily?.wasteGrams ?? [],
        spools: spools.filter((s) => s.filamentVariantId === variant.id),
      };
    })
    .sort(
      (a, b) =>
        (b.stock?.usedLast30DaysGrams ?? 0) -
          (a.stock?.usedLast30DaysGrams ?? 0) ||
        (b.stock?.onHandGrams ?? 0) - (a.stock?.onHandGrams ?? 0) ||
        (a.variant.colorName ?? '').localeCompare(b.variant.colorName ?? ''),
    );
  // One scale for every color's bars, so a prototype color reads as small next to the main one.
  const peak = Math.max(0, ...rows.flatMap((row) => row.drawn));

  return (
    <div className="overflow-hidden rounded-md border">
      <div
        className={cn(
          'hidden gap-x-4 border-b bg-muted/40 px-4 py-2 text-xs text-muted-foreground md:grid',
          COLUMNS,
        )}
      >
        <span>Color</span>
        <span>Days left</span>
        <span className="text-right">On hand</span>
        <span>Used 30 d</span>
        <span className="sr-only">Actions</span>
      </div>
      <ul aria-label="Colors">
        {rows.map((row) => (
          <ColorRow
            key={row.variant.id}
            row={row}
            filament={filament}
            days={days}
            peak={peak}
          />
        ))}
      </ul>
    </div>
  );
}

function ColorRow({
  row,
  filament,
  days,
  peak,
}: {
  row: ColorRowData;
  filament: FilamentResponse;
  days: string[];
  peak: number;
}) {
  const [open, setOpen] = useState(false);
  const { variant, stock } = row;
  const name = variant.colorName ?? 'Color';
  const used = sum(row.drawn);
  const wastePct = used > 0 ? Math.round((sum(row.waste) / used) * 100) : 0;

  return (
    <li className="border-b last:border-b-0">
      <Collapsible open={open} onOpenChange={setOpen}>
        <div
          className={cn(
            "grid grid-cols-[minmax(0,1fr)_auto] items-center gap-x-4 gap-y-2 px-4 py-3 [grid-template-areas:'id_act''run_run''stock_trend'] md:[grid-template-areas:'id_run_stock_trend_act']",
            COLUMNS,
          )}
        >
          <div className="flex min-w-0 flex-wrap items-center gap-2 [grid-area:id]">
            <Swatch colorHex={variant.colorHex} size="lg" />
            <span className="truncate text-sm font-medium">{name}</span>
            {stock?.isLow && <StatusBadge kind="amber" label="Low" />}
            {row.shortGrams > 0 && (
              <StatusBadge
                kind="amber"
                label={`Short ${formatWeight(row.shortGrams)}`}
              />
            )}
          </div>
          <div className="[grid-area:run]">
            <Runway
              onHand={stock?.onHandGrams ?? 0}
              daysLeft={stock?.daysLeft ?? null}
              thresholdDays={stock?.thresholdDays ?? null}
              colorHex={variant.colorHex}
              low={stock?.isLow ?? false}
            />
          </div>
          <div className="text-[13px] tabular-nums [grid-area:stock] md:text-right">
            <div>{formatWeight(stock?.onHandGrams ?? 0)}</div>
            <div className="text-xs text-muted-foreground">
              {pluralize(stock?.activeSpoolCount ?? 0, 'spool')}
            </div>
          </div>
          <div className="[grid-area:trend]">
            {used > 0 && (
              <Trend
                days={days}
                drawn={row.drawn}
                waste={row.waste}
                peak={peak}
                colorHex={variant.colorHex}
                caption={`${formatWeight(used)} in 30 d · ${wastePct}% waste`}
              />
            )}
          </div>
          <div className="flex items-center justify-end gap-1 [grid-area:act]">
            <Button asChild variant="ghost" size="icon-sm">
              <Link
                to={addSpoolUrl(filament, variant.id)}
                aria-label={`Add ${name} spools`}
              >
                <PlusIcon />
              </Link>
            </Button>
            <CollapsibleTrigger asChild>
              <Button
                variant="ghost"
                size="icon-sm"
                aria-label={`${open ? 'Hide' : 'Show'} ${name} spools`}
              >
                <ChevronDownIcon
                  className={cn('transition-transform', open && 'rotate-180')}
                />
              </Button>
            </CollapsibleTrigger>
          </div>
        </div>
        <CollapsibleContent className="border-t bg-muted/30 px-4 py-3">
          <ColorPanel row={row} filament={filament} />
        </CollapsibleContent>
      </Collapsible>
    </li>
  );
}

/** Days left as a number and a bar toward the 60-day horizon, with a tick at the days threshold. */
function Runway({
  onHand,
  daysLeft,
  thresholdDays,
  colorHex,
  low,
}: {
  onHand: number;
  daysLeft: number | null;
  thresholdDays: number | null;
  colorHex?: string;
  low: boolean;
}) {
  if (onHand <= 0) {
    return (
      <span
        className={cn(
          'text-sm',
          low ? 'text-kind-amber' : 'text-muted-foreground',
        )}
      >
        Out of stock
      </span>
    );
  }
  if (daysLeft == null) {
    return (
      <span className="text-sm text-muted-foreground">No use in 30 d</span>
    );
  }
  const fill = (Math.min(daysLeft, HORIZON_DAYS) / HORIZON_DAYS) * 100;
  const tick =
    thresholdDays && thresholdDays > 0 && thresholdDays < HORIZON_DAYS
      ? (thresholdDays / HORIZON_DAYS) * 100
      : null;
  return (
    <div className="flex items-center gap-3">
      <span
        className={cn(
          'w-16 shrink-0 text-xl leading-none font-semibold tabular-nums',
          low && 'text-kind-amber',
        )}
      >
        {formatDays(daysLeft, HORIZON_DAYS)}
      </span>
      <span aria-hidden="true" className="relative h-2 min-w-16 flex-1">
        <span className="filament-consumed absolute inset-0 overflow-hidden rounded-sm">
          <span
            className={cn(
              'absolute inset-y-0 left-0 rounded-sm ring-1 ring-inset',
              isLight(colorHex)
                ? 'ring-foreground/40 dark:ring-foreground/20'
                : 'ring-foreground/20 dark:ring-foreground/40',
            )}
            style={{ width: `${fill}%`, backgroundColor: colorHex }}
          />
        </span>
        {tick != null && (
          <span
            className="absolute -inset-y-1 w-0.5 -translate-x-1/2 rounded-full"
            style={{
              left: `${tick}%`,
              ...(tick <= fill
                ? filamentIndicator(colorHex)
                : { backgroundColor: 'var(--foreground)' }),
            }}
          />
        )}
      </span>
    </div>
  );
}

/** Grams drawn per day as bars in the filament's color, the part failed prints took hatched on top. */
function Trend({
  days,
  drawn,
  waste,
  peak,
  colorHex,
  caption,
}: {
  days: string[];
  drawn: number[];
  waste: number[];
  peak: number;
  colorHex?: string;
  caption: string;
}) {
  return (
    <div className="flex w-40 flex-col gap-1 md:w-auto">
      <span aria-hidden="true" className="flex h-6 items-end gap-px">
        {days.map((day, index) => {
          const grams = drawn[index] ?? 0;
          const wasted = waste[index] ?? 0;
          return (
            // A faint track behind each day keeps a white or black filament's bars readable on either theme.
            <span
              key={day}
              className="flex h-full flex-1 flex-col justify-end rounded-[1px] bg-foreground/[0.06]"
            >
              {grams > 0 && (
                <span
                  className={cn(
                    // A border, not an inset ring: the bar's own fill would cover the ring.
                    'flex flex-col overflow-hidden rounded-[1px] border',
                    isLight(colorHex)
                      ? 'border-foreground/40 dark:border-foreground/20'
                      : 'border-foreground/20 dark:border-foreground/40',
                  )}
                  style={{ height: `max(2px, ${(grams / peak) * 100}%)` }}
                >
                  {wasted > 0 && (
                    <span
                      className="filament-consumed shrink-0"
                      style={{ height: `${(wasted / grams) * 100}%` }}
                    />
                  )}
                  <span
                    className="flex-1"
                    style={{ backgroundColor: colorHex }}
                  />
                </span>
              )}
            </span>
          );
        })}
      </span>
      <span className="text-xs text-muted-foreground tabular-nums">
        {caption}
      </span>
    </div>
  );
}

/** The color's spools in printers, its shelf stock with identical full rolls counted, and its value. */
function ColorPanel({
  row,
  filament,
}: {
  row: ColorRowData;
  filament: FilamentResponse;
}) {
  const currency = useCurrency();
  const name = row.variant.colorName ?? 'color';
  const inPrinters = row.spools.filter((s) => s.state === 'Using');
  const shelf = groupShelf(row.spools.filter((s) => s.state === 'InStorage'));
  const price = row.variant.pricePerKg ?? filament.pricePerKg;
  const value =
    row.stock?.activeSpoolCount && row.stock.stockValue != null
      ? `Value ${formatCost(row.stock.stockValue, currency)}${(row.stock.unpricedSpoolCount ?? 0) > 0 ? ' · Partial' : ''}`
      : null;
  const priceText =
    price == null
      ? 'No price'
      : `${formatCost(price, currency)}/kg${row.variant.pricePerKg == null ? ' default' : ''}`;

  return (
    <div className="flex flex-col gap-3 text-[13px]">
      <dl className="grid gap-x-6 gap-y-2 sm:grid-cols-[6.5rem_minmax(0,1fr)]">
        <dt className="text-muted-foreground">In printers</dt>
        <dd>
          {inPrinters.length === 0 ? (
            <span className="text-muted-foreground">None</span>
          ) : (
            <ul className="flex max-w-xl flex-col gap-1.5">
              {inPrinters.map((spool) => {
                const weight = spool.spoolWeightGrams ?? 0;
                const remaining = spool.remainingWeightGrams ?? 0;
                return (
                  <li
                    key={spool.id}
                    className="grid grid-cols-[minmax(0,1fr)_5rem_auto] items-center gap-3"
                  >
                    <Link
                      to={`/filament/spools/${spool.id}`}
                      className="truncate hover:underline"
                    >
                      {spool.feedSlotLabel ?? spool.label ?? name}
                    </Link>
                    <FillMeter
                      colorHex={spool.colorHex}
                      percent={weight > 0 ? (remaining / weight) * 100 : 0}
                    />
                    <span className="text-muted-foreground tabular-nums">
                      {formatGrams(remaining)} / {formatWeight(weight)}
                    </span>
                  </li>
                );
              })}
            </ul>
          )}
        </dd>
        <dt className="text-muted-foreground">On the shelf</dt>
        <dd>
          {shelf.full.length === 0 && shelf.partial.length === 0 ? (
            <span className="text-muted-foreground">None</span>
          ) : (
            <div className="flex flex-wrap items-center gap-x-4 gap-y-1">
              {shelf.full.map((group) => (
                <span key={group.key} className="tabular-nums">
                  {formatWeight(group.spoolWeightGrams)} · full ×{' '}
                  {group.spools.length}
                </span>
              ))}
              {shelf.partial.length > 0 && (
                <span className="flex flex-wrap items-center gap-x-1.5 tabular-nums">
                  {shelf.partial.length} partial:
                  {shelf.partial.map((spool) => (
                    <Link
                      key={spool.id}
                      to={`/filament/spools/${spool.id}`}
                      className="hover:underline"
                    >
                      {formatGrams(spool.remainingWeightGrams ?? 0)}
                    </Link>
                  ))}
                </span>
              )}
            </div>
          )}
        </dd>
      </dl>
      <div className="flex flex-wrap items-center justify-between gap-2 text-muted-foreground">
        <span className="tabular-nums">
          {[value, priceText].filter(Boolean).join(' · ')}
        </span>
        <span className="flex gap-4">
          <Link
            to={`?tab=spools&color=${row.variant.id}`}
            className="hover:text-foreground hover:underline"
          >
            All {name} spools
          </Link>
          <Link
            to={`?tab=history&color=${row.variant.id}`}
            className="hover:text-foreground hover:underline"
          >
            History
          </Link>
        </span>
      </div>
    </div>
  );
}
