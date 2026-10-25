import { ArrowDownIcon, ArrowUpIcon, DownloadIcon } from 'lucide-react';
import type { ReactNode } from 'react';
import { Link } from 'react-router';
import { serverListQueryOptions } from '@/components/data-table/useServerListState';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { KpiCard } from '@/components/kpi-card/KpiCard';
import { Swatch } from '@/components/swatch/Swatch';
import { Button } from '@/components/ui/button';
import {
  Card,
  CardAction,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { downloadFile } from '@/lib/api/downloadFile';
import type {
  FailureCode,
  PrinterStats,
  StatsReport,
  StatsSummary,
} from '@/lib/api/generated/endpoints.schemas';
import {
  getOpenFarmApiEndpointsReportsExportMaterialsReportEndpointUrl,
  useOpenFarmApiEndpointsReportsGetMaterialsReportEndpoint,
} from '@/lib/api/generated/reports/reports';
import { formatMinutes } from '@/lib/formatMinutes';
import {
  formatCost,
  formatHours,
  formatPercent,
  formatWeight,
} from '@/lib/formatUnits';
import { cn } from '@/lib/utils';
import { observedHours, Sparkline, TIME_STATES, TimeBar } from './StatsCharts';

const ratio = (part: number, whole: number) =>
  whole > 0 ? part / whole : null;

/**
 * The change against the previous range: an arrow and the amount, in the good or bad color by whether up is good.
 * `points` compares shares in percentage points.
 */
function Delta({
  current,
  previous,
  points = false,
  upIsGood = true,
}: {
  current: number | null | undefined;
  previous: number | null | undefined;
  points?: boolean;
  upIsGood?: boolean;
}) {
  if (current == null || previous == null) return <span>—</span>;
  const change = points
    ? (current - previous) * 100
    : previous === 0
      ? null
      : (current - previous) / Math.abs(previous);
  if (change === null)
    return <span>{current === 0 ? 'No change' : 'New'}</span>;
  if (Math.abs(change) < (points ? 0.05 : 0.0005))
    return <span>No change</span>;
  const up = change > 0;
  const Icon = up ? ArrowUpIcon : ArrowDownIcon;
  const amount = points
    ? `${Math.abs(change).toFixed(1)} pt`
    : formatPercent(Math.abs(change));
  return (
    <span
      className={cn(
        'inline-flex items-center gap-0.5 font-medium',
        up === upIsGood ? 'text-status-complete' : 'text-status-offline',
      )}
    >
      <Icon aria-hidden className="size-3" />
      <span className="sr-only">{up ? 'Up' : 'Down'}</span>
      {amount}
    </span>
  );
}

function Tile({
  label,
  value,
  delta,
  trend,
}: {
  label: string;
  value: ReactNode;
  delta: ReactNode;
  trend: (number | null)[];
}) {
  return (
    <KpiCard
      label={label}
      value={value}
      sub={
        <span className="flex items-end justify-between gap-2">
          {delta}
          <Sparkline values={trend} />
        </span>
      }
    />
  );
}

/** Prints · Success · Utilization · Filament · Waste · Units, each against the previous range, with its daily trend. */
export function StatsKpis({ report }: { report: StatsReport }) {
  const current = report.current ?? {};
  const previous = report.previous ?? {};
  const days = report.days ?? [];
  const prints = (day: (typeof days)[number]) =>
    (day.completed ?? 0) + (day.failed ?? 0) + (day.cancelled ?? 0);
  return (
    <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3 2xl:grid-cols-6">
      <Tile
        label="Prints"
        value={(current.prints ?? 0).toLocaleString()}
        delta={<Delta current={current.prints} previous={previous.prints} />}
        trend={days.map(prints)}
      />
      <Tile
        label="Success"
        value={
          current.successRate == null ? '—' : formatPercent(current.successRate)
        }
        delta={
          <Delta
            current={current.successRate}
            previous={previous.successRate}
            points
          />
        }
        trend={days.map((day) => ratio(day.completed ?? 0, prints(day)))}
      />
      <Tile
        label="Utilization"
        value={
          current.activity?.utilization == null
            ? '—'
            : formatPercent(current.activity.utilization)
        }
        delta={
          <Delta
            current={current.activity?.utilization}
            previous={previous.activity?.utilization}
            points
          />
        }
        trend={days.map((day) =>
          ratio(day.printingHours ?? 0, day.observedHours ?? 0),
        )}
      />
      <Tile
        label="Filament"
        value={formatWeight(current.usedGrams ?? 0)}
        delta={
          <Delta current={current.usedGrams} previous={previous.usedGrams} />
        }
        trend={days.map((day) => day.usedGrams ?? 0)}
      />
      <Tile
        label="Waste"
        value={formatWeight(current.wasteGrams ?? 0)}
        delta={
          <Delta
            current={current.wasteGrams}
            previous={previous.wasteGrams}
            upIsGood={false}
          />
        }
        trend={days.map((day) => day.wasteGrams ?? 0)}
      />
      <Tile
        label="Units made"
        value={(current.units ?? 0).toLocaleString()}
        delta={<Delta current={current.units} previous={previous.units} />}
        trend={days.map((day) => day.units ?? 0)}
      />
    </div>
  );
}

/** Label and value rows, values right-aligned. */
function Figures({ rows }: { rows: [label: string, value: ReactNode][] }) {
  return (
    <dl className="grid grid-cols-[1fr_auto] gap-x-4 gap-y-1.5">
      {rows.map(([label, value]) => (
        <div key={label} className="contents">
          <dt className="text-muted-foreground">{label}</dt>
          <dd className="text-right tabular-nums">{value}</dd>
        </div>
      ))}
    </dl>
  );
}

/**
 * The fleet's observed time by printer state, each state's hours and share; the hours nothing was observed; and how long
 * plates waited for Mark ready.
 */
export function PrinterTimeCard({ summary }: { summary: StatsSummary }) {
  const activity = summary.activity;
  const total = observedHours(activity);
  return (
    <Card>
      <CardHeader>
        <CardTitle>Printer time</CardTitle>
        <CardDescription>All printers</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        <TimeBar activity={activity} className="h-3" />
        <dl className="grid grid-cols-[auto_1fr_auto_auto] items-center gap-x-3 gap-y-1.5">
          {TIME_STATES.map((state) => {
            const hours = activity?.[state.key] ?? 0;
            return (
              <div key={state.key} className="contents">
                <span
                  aria-hidden
                  className="size-2 rounded-[2px]"
                  style={{ backgroundColor: state.color }}
                />
                <dt className="text-muted-foreground">{state.label}</dt>
                <dd className="text-right tabular-nums">
                  {formatHours(hours)}
                </dd>
                <dd className="w-10 text-right text-muted-foreground tabular-nums">
                  {total > 0 ? formatPercent(hours / total) : '—'}
                </dd>
              </div>
            );
          })}
        </dl>
        <Figures
          rows={[
            ['No data', formatHours(activity?.noData ?? 0)],
            [
              'Plate clear, median',
              summary.plateClearMinutes == null
                ? '—'
                : formatMinutes(summary.plateClearMinutes),
            ],
          ]}
        />
      </CardContent>
    </Card>
  );
}

/** What failed: outcomes, the filament and printer time failed and cancelled prints took, and the codes printers reported. */
export function ReliabilityCard({
  summary,
  codes,
}: {
  summary: StatsSummary;
  codes: FailureCode[];
}) {
  return (
    <Card>
      <CardHeader>
        <CardTitle>Reliability</CardTitle>
        <CardDescription>Failed and cancelled prints</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        <Figures
          rows={[
            ['Failed', (summary.failed ?? 0).toLocaleString()],
            ['Cancelled', (summary.cancelled ?? 0).toLocaleString()],
            [
              'Waste',
              `${formatWeight(summary.wasteGrams ?? 0)}${summary.wasteShare == null ? '' : ` · ${formatPercent(summary.wasteShare)}`}`,
            ],
            ['Time lost', formatHours(summary.wasteMachineHours ?? 0)],
          ]}
        />
        <div className="flex flex-col gap-1.5">
          <h3 className="text-xs font-medium text-muted-foreground">
            Top failure codes
          </h3>
          {codes.length === 0 ? (
            <p className="text-muted-foreground">None reported.</p>
          ) : (
            <ul className="flex flex-col gap-1.5">
              {codes.map((code) => (
                <li key={code.code} className="flex items-baseline gap-2">
                  <span className="font-mono text-xs">{code.code}</span>
                  <span className="min-w-0 flex-1 truncate text-muted-foreground">
                    {code.message}
                  </span>
                  <span className="tabular-nums">{code.prints}</span>
                </li>
              ))}
            </ul>
          )}
        </div>
      </CardContent>
    </Card>
  );
}

/** Farm-wide filament from the materials report: totals, spend, and the colors used most; detail is on each filament's page. */
export function MaterialsCard({
  range,
}: {
  range: { from: string; to: string };
}) {
  const query = useOpenFarmApiEndpointsReportsGetMaterialsReportEndpoint(
    range,
    { query: serverListQueryOptions },
  );
  const report = query.data?.status === 200 ? query.data.data : undefined;
  const total = report?.total;
  const colors = (report?.rows ?? [])
    .filter((row) => row.filamentVariantId)
    .slice(0, 5);
  const peak = Math.max(
    1,
    ...colors.map((row) => (row.usedGrams ?? 0) + (row.wasteGrams ?? 0)),
  );
  const unpriced = total?.unpricedGrams ?? 0;
  return (
    <Card>
      <CardHeader>
        <CardTitle>Materials</CardTitle>
        <CardDescription>
          <Link to="/filament" className="underline-offset-4 hover:underline">
            By filament
          </Link>
        </CardDescription>
        <CardAction>
          <Button
            variant="outline"
            size="sm"
            onClick={() =>
              downloadFile(
                getOpenFarmApiEndpointsReportsExportMaterialsReportEndpointUrl(
                  range,
                ),
                'materials-report.csv',
                'Could not export CSV.',
              )
            }
          >
            <DownloadIcon data-icon="inline-start" />
            Export CSV
          </Button>
        </CardAction>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        {query.isLoading ? (
          <Skeleton className="h-40 w-full" />
        ) : !report ? (
          <ErrorState
            description="Could not load materials."
            onRetry={() => query.refetch()}
          />
        ) : (
          <>
            <Figures
              rows={[
                ['Used', formatWeight(total?.usedGrams ?? 0)],
                ['Waste', formatWeight(total?.wasteGrams ?? 0)],
                ['Written off', formatWeight(total?.writtenOffGrams ?? 0)],
                [
                  'Spend',
                  <>
                    {formatCost(total?.cost ?? 0)}
                    {unpriced > 0 && (
                      <span className="block text-xs text-muted-foreground">
                        Partial · {formatWeight(unpriced)} unpriced
                      </span>
                    )}
                  </>,
                ],
              ]}
            />
            {colors.length > 0 && (
              <ul className="flex flex-col gap-2" aria-label="Top colors">
                {colors.map((row) => {
                  const grams = (row.usedGrams ?? 0) + (row.wasteGrams ?? 0);
                  return (
                    <li
                      key={row.filamentVariantId}
                      className="flex flex-col gap-1"
                    >
                      <span className="flex items-center gap-2">
                        <Swatch
                          colorHex={row.colorHex ?? undefined}
                          size="sm"
                        />
                        <span className="min-w-0 flex-1 truncate">{`${row.material} · ${row.colorName}`}</span>
                        <span className="tabular-nums">
                          {formatWeight(grams)}
                        </span>
                      </span>
                      <span aria-hidden className="h-1 rounded-full bg-muted">
                        <span
                          className="block h-full rounded-full bg-muted-foreground"
                          style={{ width: `${(grams / peak) * 100}%` }}
                        />
                      </span>
                    </li>
                  );
                })}
              </ul>
            )}
          </>
        )}
      </CardContent>
    </Card>
  );
}

/** Flow through the queue: how long jobs waited to start, and how their print time ran against the part's estimate. */
export function QueueCard({ summary }: { summary: StatsSummary }) {
  const wait = (minutes: number | null | undefined) =>
    minutes == null ? '—' : formatMinutes(minutes);
  return (
    <Card>
      <CardHeader>
        <CardTitle>Queue</CardTitle>
      </CardHeader>
      <CardContent>
        <Figures
          rows={[
            ['Wait, median', wait(summary.queueWaitP50Minutes)],
            ['Wait, 95th pct', wait(summary.queueWaitP95Minutes)],
            [
              'Print time ÷ estimate',
              summary.estimateRatio == null
                ? '—'
                : `${summary.estimateRatio.toFixed(2)}×`,
            ],
          ]}
        />
      </CardContent>
    </Card>
  );
}

/** Every printer's output and time, most printing first. */
export function PrintersCard({ printers }: { printers: PrinterStats[] }) {
  const rows = [...printers].sort(
    (a, b) => (b.activity?.printing ?? 0) - (a.activity?.printing ?? 0),
  );
  return (
    <Card>
      <CardHeader>
        <CardTitle>Printers</CardTitle>
      </CardHeader>
      <CardContent>
        {rows.length === 0 ? (
          <EmptyState size="sm" title="No printers." />
        ) : (
          <div className="overflow-x-auto">
            <Table aria-label="Printers">
              <TableHeader>
                <TableRow>
                  <TableHead>Printer</TableHead>
                  <TableHead className="text-right">Prints</TableHead>
                  <TableHead className="text-right">Success</TableHead>
                  <TableHead className="text-right">Printing</TableHead>
                  <TableHead className="text-right">Utilization</TableHead>
                  <TableHead className="text-right">Filament</TableHead>
                  <TableHead className="w-40">Time</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {rows.map((printer) => {
                  const success = ratio(
                    printer.completed ?? 0,
                    printer.prints ?? 0,
                  );
                  return (
                    <TableRow key={printer.printerId}>
                      <TableCell>
                        <Link
                          to={`/printers/${printer.printerId}`}
                          className="underline-offset-4 hover:underline"
                        >
                          {printer.name}
                        </Link>
                      </TableCell>
                      <TableCell className="text-right tabular-nums">
                        {printer.prints ?? 0}
                      </TableCell>
                      <TableCell className="text-right tabular-nums">
                        {success == null ? '—' : formatPercent(success)}
                      </TableCell>
                      <TableCell className="text-right tabular-nums">
                        {formatHours(printer.activity?.printing ?? 0)}
                      </TableCell>
                      <TableCell className="text-right tabular-nums">
                        {printer.activity?.utilization == null
                          ? '—'
                          : formatPercent(printer.activity.utilization)}
                      </TableCell>
                      <TableCell className="text-right tabular-nums">
                        {formatWeight(printer.usedGrams ?? 0)}
                      </TableCell>
                      <TableCell>
                        <TimeBar activity={printer.activity} />
                      </TableCell>
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
          </div>
        )}
      </CardContent>
    </Card>
  );
}
