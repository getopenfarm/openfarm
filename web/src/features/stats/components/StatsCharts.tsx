import {
  Bar,
  BarChart,
  CartesianGrid,
  Line,
  LineChart,
  XAxis,
  YAxis,
} from 'recharts';
import {
  type ChartConfig,
  ChartContainer,
  ChartLegend,
  ChartLegendContent,
  ChartTooltip,
  ChartTooltipContent,
} from '@/components/ui/chart';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import type {
  ActivityHours,
  StatsDay,
} from '@/lib/api/generated/endpoints.schemas';
import { formatHours, formatPercent } from '@/lib/formatUnits';
import { cn } from '@/lib/utils';

/**
 * Where observed printer time went, in the order the bars stack; colors are the printer-status hues (index.css). Shares are of
 * observed time, as utilization is, so Printing's share is the printer's utilization; time with no data stays out of the bar.
 */
export const TIME_STATES = [
  { key: 'printing', label: 'Printing', color: 'var(--chart-printing)' },
  {
    key: 'awaitingClear',
    label: 'Awaiting clear',
    color: 'var(--chart-awaiting-clear)',
  },
  { key: 'paused', label: 'Paused', color: 'var(--chart-paused)' },
  { key: 'idle', label: 'Idle', color: 'var(--chart-idle)' },
  { key: 'offline', label: 'Offline', color: 'var(--chart-offline)' },
] as const satisfies readonly {
  key: keyof ActivityHours;
  label: string;
  color: string;
}[];

/** The hours something was known about the printer: every state above. */
export const observedHours = (activity: ActivityHours | undefined) =>
  TIME_STATES.reduce((sum, state) => sum + (activity?.[state.key] ?? 0), 0);

/** 'YYYY-MM-DD' -> "Oct 12", read as a local date. */
export function formatDay(day: string | undefined) {
  if (!day) return '';
  const [year, month, date] = day.split('-').map(Number);
  return new Date(year, month - 1, date).toLocaleDateString(undefined, {
    month: 'short',
    day: 'numeric',
  });
}

/** A tile's trend: one value per day, a gap where a day has none. Decorative; the charts and tables carry the values. */
export function Sparkline({ values }: { values: (number | null)[] }) {
  return (
    <ChartContainer
      config={{ value: { color: 'var(--muted-foreground)' } }}
      className="aspect-auto h-8 w-24 shrink-0"
      initialDimension={{ width: 96, height: 32 }}
      aria-hidden
    >
      <LineChart
        data={values.map((value, index) => ({ index, value }))}
        margin={{ top: 3, right: 3, bottom: 3, left: 3 }}
        accessibilityLayer={false}
      >
        <Line
          dataKey="value"
          type="monotone"
          stroke="var(--color-value)"
          strokeWidth={1.5}
          dot={false}
          isAnimationActive={false}
        />
      </LineChart>
    </ChartContainer>
  );
}

const OUTCOMES = {
  completed: { label: 'Completed', color: 'var(--chart-completed)' },
  cancelled: { label: 'Cancelled', color: 'var(--chart-cancelled)' },
  failed: { label: 'Failed', color: 'var(--chart-failed)' },
} satisfies ChartConfig;

/** Bottom to top; the legend follows the stack. */
const OUTCOME_KEYS = Object.keys(OUTCOMES) as (keyof typeof OUTCOMES)[];

/**
 * Prints per day, stacked by outcome; red and green never touch, so the stack reads under red-green color blindness. A
 * table below holds the same numbers.
 */
export function OutcomeChart({ days }: { days: StatsDay[] }) {
  const data = days.map((day) => ({
    day: day.day,
    completed: day.completed ?? 0,
    cancelled: day.cancelled ?? 0,
    failed: day.failed ?? 0,
  }));
  return (
    <div className="flex flex-col gap-2">
      <ChartContainer config={OUTCOMES} className="aspect-auto h-60 w-full">
        <BarChart data={data} margin={{ top: 4, right: 4, left: -20 }}>
          <CartesianGrid vertical={false} />
          <XAxis
            dataKey="day"
            tickLine={false}
            axisLine={false}
            tickMargin={8}
            minTickGap={24}
            tickFormatter={formatDay}
          />
          <YAxis
            allowDecimals={false}
            tickLine={false}
            axisLine={false}
            width={44}
          />
          <ChartTooltip
            cursor={false}
            content={
              <ChartTooltipContent
                labelFormatter={(_, payload) =>
                  formatDay(payload?.[0]?.payload?.day)
                }
              />
            }
          />
          <ChartLegend
            content={<ChartLegendContent />}
            itemSorter={(item) =>
              OUTCOME_KEYS.indexOf(item.dataKey as keyof typeof OUTCOMES)
            }
          />
          {OUTCOME_KEYS.map((outcome) => (
            <Bar
              key={outcome}
              dataKey={outcome}
              stackId="outcome"
              fill={`var(--color-${outcome})`}
              // A 2px gap in the card's color between stacked segments, not a border.
              stroke="var(--card)"
              strokeWidth={2}
              radius={[3, 3, 0, 0]}
              maxBarSize={24}
              isAnimationActive={false}
            />
          ))}
        </BarChart>
      </ChartContainer>
      <details className="text-xs">
        <summary className="cursor-pointer text-muted-foreground">
          Table
        </summary>
        <Table aria-label="Prints per day">
          <TableHeader>
            <TableRow>
              <TableHead>Day</TableHead>
              {Object.values(OUTCOMES).map((outcome) => (
                <TableHead key={outcome.label} className="text-right">
                  {outcome.label}
                </TableHead>
              ))}
            </TableRow>
          </TableHeader>
          <TableBody>
            {data.map((row) => (
              <TableRow key={row.day}>
                <TableCell>{formatDay(row.day)}</TableCell>
                <TableCell className="text-right tabular-nums">
                  {row.completed}
                </TableCell>
                <TableCell className="text-right tabular-nums">
                  {row.cancelled}
                </TableCell>
                <TableCell className="text-right tabular-nums">
                  {row.failed}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </details>
    </div>
  );
}

/** One printer's (or the fleet's) observed time as a 100% bar, a 2px gap between states; empty when nothing is known. */
export function TimeBar({
  activity,
  className,
}: {
  activity: ActivityHours | undefined;
  className?: string;
}) {
  const total = observedHours(activity);
  const shares = TIME_STATES.map((state) => ({
    ...state,
    hours: activity?.[state.key] ?? 0,
  })).filter((state) => state.hours > 0);
  return (
    <div
      role="img"
      aria-label={
        shares
          .map(
            (state) => `${state.label} ${formatPercent(state.hours / total)}`,
          )
          .join(', ') || 'No data'
      }
      className={cn(
        'flex h-2 gap-0.5 overflow-hidden rounded-full',
        shares.length === 0 && 'bg-muted',
        className,
      )}
    >
      {shares.map((state) => (
        <span
          key={state.key}
          title={`${state.label} · ${formatHours(state.hours)} · ${formatPercent(state.hours / total)}`}
          className="h-full min-w-0.5 basis-0"
          style={{ flexGrow: state.hours, backgroundColor: state.color }}
        />
      ))}
    </div>
  );
}
