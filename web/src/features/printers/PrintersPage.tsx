import { PlusIcon, PrinterIcon, TriangleAlertIcon } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useNavigate } from 'react-router';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { MAINTENANCE_FLAG_SEEDS } from '@/features/maintenance/data/mockMaintenance';
import { matchMaintenanceToFleet } from '@/features/maintenance/lib/matchMaintenanceToFleet';
import type { ListFleetResponse } from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsPrintersListFleetEndpoint } from '@/lib/api/generated/printers/printers';
import {
  PrinterFilterBar,
  type PrinterStatusFilter,
} from './components/PrinterFilterBar';
import { PrintersTable } from './components/PrintersTable';

export function PrintersPage() {
  const navigate = useNavigate();
  const fleetQuery = useOpenFarmApiEndpointsPrintersListFleetEndpoint();

  const [query, setQuery] = useState('');
  const [statusFilter, setStatusFilter] = useState<PrinterStatusFilter>('all');

  const printers =
    (fleetQuery.data?.data as ListFleetResponse | undefined)?.printers ?? [];

  const maintenanceByPrinterId = useMemo(() => {
    const flags = matchMaintenanceToFleet(printers, MAINTENANCE_FLAG_SEEDS);
    return new Map(flags.map((flag) => [flag.printerId, flag]));
  }, [printers]);

  const counts = useMemo(
    () => ({
      all: printers.length,
      printing: printers.filter((p) => p.status === 'Printing').length,
      idle: printers.filter(
        (p) => p.status === 'Idle' && p.isReachable !== false,
      ).length,
      awaiting: printers.filter((p) => p.status === 'AwaitingClear').length,
      maintenance: maintenanceByPrinterId.size,
      offline: printers.filter((p) => p.isReachable === false).length,
    }),
    [printers, maintenanceByPrinterId],
  );

  const filteredPrinters = useMemo(() => {
    const normalizedQuery = query.trim().toLowerCase();
    return printers.filter((printer) => {
      if (normalizedQuery) {
        const haystack =
          `${printer.name ?? ''} ${printer.modelCode ?? ''}`.toLowerCase();
        if (!haystack.includes(normalizedQuery)) return false;
      }
      switch (statusFilter) {
        case 'printing':
          return printer.status === 'Printing';
        case 'idle':
          return printer.status === 'Idle' && printer.isReachable !== false;
        case 'awaiting':
          return printer.status === 'AwaitingClear';
        case 'maintenance':
          return maintenanceByPrinterId.has(printer.id ?? '');
        case 'offline':
          return printer.isReachable === false;
        default:
          return true;
      }
    });
  }, [printers, query, statusFilter, maintenanceByPrinterId]);

  return (
    <Page>
      <PageHeader
        title="Printers"
        action={
          <Button onClick={() => navigate('/printers/new')}>
            <PlusIcon data-icon="inline-start" /> Add printer
          </Button>
        }
      />

      {fleetQuery.isLoading ? (
        <Skeleton className="h-64 w-full" />
      ) : fleetQuery.isError ? (
        <ErrorState
          icon={<TriangleAlertIcon className="size-8 text-destructive" />}
          description="Could not load printers."
          onRetry={() => fleetQuery.refetch()}
        />
      ) : printers.length === 0 ? (
        <EmptyState
          icon={<PrinterIcon className="size-8 text-muted-foreground" />}
          title="No printers"
          description="Add one to see live status."
          action={
            <Button onClick={() => navigate('/printers/new')}>
              <PlusIcon data-icon="inline-start" /> Add printer
            </Button>
          }
        />
      ) : (
        <div className="flex flex-col gap-4">
          <PrinterFilterBar
            query={query}
            onQueryChange={setQuery}
            statusFilter={statusFilter}
            onStatusFilterChange={setStatusFilter}
            counts={counts}
          />

          {filteredPrinters.length === 0 ? (
            <EmptyState
              title="No printers match"
              description="Change the search or filter."
            />
          ) : (
            <PrintersTable
              printers={filteredPrinters}
              maintenanceByPrinterId={maintenanceByPrinterId}
              density="medium"
              onOpen={(id) => navigate(`/printers/${id}`)}
            />
          )}
        </div>
      )}
    </Page>
  );
}
