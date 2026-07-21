import { useQueryClient } from '@tanstack/react-query';
import { PrinterIcon, TriangleAlertIcon } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useNavigate } from 'react-router';
import { toast } from 'sonner';
import { DensityToggle } from '@/components/density-toggle/DensityToggle';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { PageHeader } from '@/components/page-header/PageHeader';
import { SkeletonGrid } from '@/components/skeleton-grid/SkeletonGrid';
import { ViewToggle } from '@/components/view-toggle/ViewToggle';
import { MAINTENANCE_FLAG_SEEDS } from '@/features/maintenance/data/mockMaintenance';
import { matchMaintenanceToFleet } from '@/features/maintenance/lib/matchMaintenanceToFleet';
import type { ListFleetResponse } from '@/lib/api/generated/endpoints.schemas';
import {
  getOpenFarmApiEndpointsPrintersListFleetEndpointQueryKey,
  useOpenFarmApiEndpointsPrintersListFleetEndpoint,
  useOpenFarmApiEndpointsPrintersMarkPrinterReadyEndpoint,
} from '@/lib/api/generated/printers/printers';
import { usePreferences } from '@/lib/preferences/usePreferences';
import { AddPrinterDialog } from './components/AddPrinterDialog';
import {
  PrinterFilterBar,
  type PrinterStatusFilter,
} from './components/PrinterFilterBar';
import { PrintersGrid } from './components/PrintersGrid';
import { PrintersTable } from './components/PrintersTable';

export function PrintersPage() {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const fleetQuery = useOpenFarmApiEndpointsPrintersListFleetEndpoint();
  const { getListView, getListDensity } = usePreferences();
  const view = getListView('printers');
  const density = getListDensity('printers');

  const [query, setQuery] = useState('');
  const [statusFilter, setStatusFilter] = useState<PrinterStatusFilter>('all');

  const markReadyMutation =
    useOpenFarmApiEndpointsPrintersMarkPrinterReadyEndpoint({
      mutation: {
        onSuccess: () => {
          toast.success('Printer marked ready.');
          queryClient.invalidateQueries({
            queryKey:
              getOpenFarmApiEndpointsPrintersListFleetEndpointQueryKey(),
          });
        },
        onError: () => {
          toast.error('Could not mark printer ready.');
        },
      },
    });

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
    <div className="flex flex-col gap-6">
      <PageHeader
        title="Printers"
        description="Fleet status updates live — no refresh needed."
        action={<AddPrinterDialog />}
      />

      {fleetQuery.isLoading ? (
        <SkeletonGrid />
      ) : fleetQuery.isError ? (
        <ErrorState
          icon={<TriangleAlertIcon className="size-8 text-destructive" />}
          description="Couldn't load printers."
          onRetry={() => fleetQuery.refetch()}
        />
      ) : printers.length === 0 ? (
        <EmptyState
          icon={<PrinterIcon className="size-8 text-muted-foreground" />}
          title="No printers yet"
          description="No printers registered yet. Add one to see its live status here."
          action={<AddPrinterDialog />}
        />
      ) : (
        <div className="flex flex-col gap-4">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <PrinterFilterBar
              query={query}
              onQueryChange={setQuery}
              statusFilter={statusFilter}
              onStatusFilterChange={setStatusFilter}
              counts={counts}
            />
            <div className="flex items-center gap-2">
              <DensityToggle page="printers" />
              <ViewToggle page="printers" />
            </div>
          </div>

          {filteredPrinters.length === 0 ? (
            <EmptyState
              title="No printers match"
              description="Try a different search or status filter."
            />
          ) : view === 'table' ? (
            <PrintersTable
              printers={filteredPrinters}
              maintenanceByPrinterId={maintenanceByPrinterId}
              density={density}
              onOpen={(id) => navigate(`/printers/${id}`)}
              onMarkReady={(id) => markReadyMutation.mutate({ id })}
            />
          ) : (
            <PrintersGrid
              printers={filteredPrinters}
              density={density}
              onMarkReady={(id) => markReadyMutation.mutate({ id })}
            />
          )}
        </div>
      )}
    </div>
  );
}
