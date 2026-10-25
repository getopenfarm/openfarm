import { useState } from 'react';
import { serverListQueryOptions } from '@/components/data-table/useServerListState';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import { SkeletonGrid } from '@/components/skeleton-grid/SkeletonGrid';
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { useOpenFarmApiEndpointsReportsStatsEndpoint } from '@/lib/api/generated/reports/reports';
import { cn } from '@/lib/utils';
import {
  defaultRange,
  RangeFilter,
  rangeInstants,
} from './components/RangeFilter';
import {
  MaterialsCard,
  PrintersCard,
  PrinterTimeCard,
  QueueCard,
  ReliabilityCard,
  StatsKpis,
} from './components/StatsCards';
import { OutcomeChart } from './components/StatsCharts';

const DAY_MS = 86_400_000;

/**
 * The farm at a glance over a range of local days: output, reliability, printer time and filament, each against the
 * range before. Running prints and open printer stretches grow with time, so the page refetches every minute.
 */
export function StatsPage() {
  const [range, setRange] = useState(defaultRange);
  const instants = rangeInstants(range);
  const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;
  const days = Math.round(
    (Date.parse(instants.to) - Date.parse(instants.from)) / DAY_MS,
  );
  const query = useOpenFarmApiEndpointsReportsStatsEndpoint(
    { ...instants, timeZone },
    { query: { ...serverListQueryOptions, refetchInterval: 60_000 } },
  );
  const report = query.data?.status === 200 ? query.data.data : undefined;
  const current = report?.current ?? {};

  return (
    <Page>
      <PageHeader
        title="Stats"
        description={`Days in ${timeZone} · vs previous ${days} d`}
        action={
          <div className="flex flex-wrap items-center gap-2">
            <RangeFilter value={range} onChange={setRange} />
          </div>
        }
      />
      {query.isLoading ? (
        <div className="flex flex-col gap-4">
          <SkeletonGrid
            count={6}
            className="lg:grid-cols-3 xl:grid-cols-3 2xl:grid-cols-6"
            itemClassName="h-28"
          />
          <Skeleton className="h-80 w-full" />
        </div>
      ) : !report ? (
        <ErrorState
          description="Could not load stats."
          onRetry={() => query.refetch()}
        />
      ) : (
        // A new range keeps the last figures on screen, dimmed, until it loads.
        <div
          className={cn(
            'flex flex-col gap-4 transition-opacity',
            query.isFetching && query.isPlaceholderData && 'opacity-60',
          )}
          aria-busy={query.isFetching || undefined}
        >
          <StatsKpis report={report} />
          <div className="grid gap-4 lg:grid-cols-3">
            <Card className="lg:col-span-2">
              <CardHeader>
                <CardTitle>Prints per day</CardTitle>
                <CardDescription>
                  By outcome · counted on end day
                </CardDescription>
              </CardHeader>
              <CardContent>
                {(current.prints ?? 0) === 0 ? (
                  <EmptyState
                    size="sm"
                    title="No prints ended in this range."
                  />
                ) : (
                  <OutcomeChart days={report.days ?? []} />
                )}
              </CardContent>
            </Card>
            <PrinterTimeCard summary={current} />
          </div>
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
            <ReliabilityCard
              summary={current}
              codes={report.failureCodes ?? []}
            />
            <MaterialsCard range={instants} />
            <QueueCard summary={current} />
          </div>
          <PrintersCard printers={report.printers ?? []} />
        </div>
      )}
    </Page>
  );
}
