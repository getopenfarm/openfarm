import { useQueries, useQuery } from '@tanstack/react-query';
import { Printer } from 'lucide-react';
import { Link } from 'react-router';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import {
  type FarmConnectionStatus,
  farmConnectionQueryKey,
} from '@/features/printers/hooks/useFarmHub';
import { useMarkPrinterReady } from '@/features/printers/hooks/useMarkPrinterReady';
import type {
  ListFleetResponse,
  PrinterDetailResponse,
} from '@/lib/api/generated/endpoints.schemas';
import {
  getOpenFarmApiEndpointsPrintersGetPrinterDetailEndpointQueryOptions,
  useOpenFarmApiEndpointsPrintersListFleetEndpoint,
} from '@/lib/api/generated/printers/printers';
import { MonitoringCard } from './components/MonitoringCard';
import type { MonitoredPrinter } from './lib/monitoring';

const GRID =
  'grid grid-cols-1 items-stretch gap-3 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4';
export function DashboardPage() {
  const fleetQuery = useOpenFarmApiEndpointsPrintersListFleetEndpoint();
  const printers = [
    ...((fleetQuery.data?.data as ListFleetResponse | undefined)?.printers ??
      []),
  ]
    .filter((printer) => printer.id)
    .sort(
      (a, b) =>
        (a.name ?? '').localeCompare(b.name ?? '') ||
        (a.id ?? '').localeCompare(b.id ?? ''),
    );
  const details = useQueries({
    queries: printers.map((printer) =>
      getOpenFarmApiEndpointsPrintersGetPrinterDetailEndpointQueryOptions(
        printer.id ?? '',
      ),
    ),
  });
  const { data: connection = 'connecting' } = useQuery<FarmConnectionStatus>({
    queryKey: farmConnectionQueryKey,
    queryFn: () => 'connecting',
    enabled: false,
    staleTime: Infinity,
  });
  const ready = useMarkPrinterReady();
  return (
    <Page>
      {ready.confirmationDialog}
      <PageHeader
        title="Dashboard"
        action={
          connection !== 'connected' && (
            <span role="status" className="text-xs text-muted-foreground">
              {connection === 'connecting'
                ? 'Connecting…'
                : connection === 'reconnecting'
                  ? 'Reconnecting · Updates may be stale'
                  : 'Disconnected · Updates may be stale'}
            </span>
          )
        }
      />
      {fleetQuery.isLoading ? (
        <div className={GRID}>
          {[1, 2, 3, 4].map((key) => (
            <Skeleton key={key} className="h-60 rounded-xl" />
          ))}
        </div>
      ) : fleetQuery.isError ? (
        <ErrorState
          title="Couldn't load your dashboard"
          onRetry={() => fleetQuery.refetch()}
        />
      ) : printers.length === 0 ? (
        <EmptyState
          icon={<Printer className="size-8 text-muted-foreground" />}
          title="Your farm starts here"
          description="Add a printer to monitor its camera and jobs."
          action={
            <Button asChild>
              <Link to="/printers/new">Add printer</Link>
            </Button>
          }
        />
      ) : (
        <div className={GRID}>
          {printers.map((printer, index) => {
            const query = details[index];
            const detail = query?.data?.data as
              | PrinterDetailResponse
              | undefined;
            const monitored: MonitoredPrinter = {
              ...printer,
              telemetry: detail?.telemetry,
              amsUnits: detail?.amsUnits,
              externalSpoolTray: detail?.externalSpoolTray,
            };
            return (
              <MonitoringCard
                key={printer.id}
                printer={monitored}
                detailError={query?.isError ?? false}
                onRetry={() => query?.refetch()}
                onMarkReady={() => ready.mutate({ id: printer.id ?? '' })}
                readyPending={ready.isPending}
              />
            );
          })}
        </div>
      )}
    </Page>
  );
}
