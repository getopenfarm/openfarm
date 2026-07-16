import { useQueryClient } from '@tanstack/react-query';
import { PrinterIcon, TriangleAlertIcon } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useNavigate } from 'react-router';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { ViewToggle } from '@/components/view-toggle/ViewToggle';
import { MAINTENANCE_FLAG_SEEDS } from '@/features/maintenance/data/mockMaintenance';
import { matchMaintenanceToFleet } from '@/features/maintenance/lib/matchMaintenanceToFleet';
import type { ListFleetResponse } from '@/lib/api/generated/endpoints.schemas';
import {
  getOpenFarmApiEndpointsPrintersListFleetEndpointQueryKey,
  useOpenFarmApiEndpointsPrintersListFleetEndpoint,
  useOpenFarmApiEndpointsPrintersMarkPrinterReadyEndpoint,
  useOpenFarmApiEndpointsPrintersRemovePrinterEndpoint,
} from '@/lib/api/generated/printers/printers';
import { usePreferences } from '@/lib/preferences/usePreferences';
import { AddPrinterDialog } from './components/AddPrinterDialog';
import { EditPrinterDialog } from './components/EditPrinterDialog';
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
  const { getListView } = usePreferences();
  const view = getListView('printers');

  const [editingPrinterId, setEditingPrinterId] = useState<string | null>(null);
  const [query, setQuery] = useState('');
  const [statusFilter, setStatusFilter] = useState<PrinterStatusFilter>('all');

  const removeMutation = useOpenFarmApiEndpointsPrintersRemovePrinterEndpoint({
    mutation: {
      onSuccess: () => {
        toast.success('Printer removed.');
        queryClient.invalidateQueries({
          queryKey: getOpenFarmApiEndpointsPrintersListFleetEndpointQueryKey(),
        });
      },
      onError: () => {
        toast.error('Could not remove printer.');
      },
    },
  });

  function handleRemove(id: string, name: string) {
    const confirmed = window.confirm(
      `Remove ${name}? This can't be undone. Removal isn't blocked by its current print status.`,
    );
    if (!confirmed) {
      return;
    }
    removeMutation.mutate({ id });
  }

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
  const editingPrinter = printers.find((p) => p.id === editingPrinterId);

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
      <div className="flex items-center justify-between">
        <div>
          <h2 className="font-heading text-2xl font-semibold">Printers</h2>
          <p className="text-sm text-muted-foreground">
            Fleet status updates live — no refresh needed.
          </p>
        </div>
        <AddPrinterDialog />
      </div>

      {fleetQuery.isLoading ? (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-3">
          {Array.from({ length: 3 }).map((_, index) => (
            // biome-ignore lint/suspicious/noArrayIndexKey: static skeleton placeholder cards
            <Skeleton key={`skeleton-${index}`} className="h-40 w-full" />
          ))}
        </div>
      ) : fleetQuery.isError ? (
        <div className="flex flex-col items-center gap-2 rounded-xl border border-dashed p-12 text-center">
          <TriangleAlertIcon className="size-8 text-destructive" />
          <p className="text-sm text-muted-foreground">
            Couldn't load printers.
          </p>
          <Button variant="outline" onClick={() => fleetQuery.refetch()}>
            Retry
          </Button>
        </div>
      ) : printers.length === 0 ? (
        <div className="flex flex-col items-center gap-2 rounded-xl border border-dashed p-12 text-center">
          <PrinterIcon className="size-8 text-muted-foreground" />
          <p className="text-sm text-muted-foreground">
            No printers registered yet. Add one to see its live status here.
          </p>
        </div>
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
            <ViewToggle page="printers" />
          </div>

          {filteredPrinters.length === 0 ? (
            <div className="flex flex-col items-center gap-1 rounded-xl border border-dashed p-12 text-center">
              <p className="text-sm font-medium">No printers match</p>
              <p className="text-sm text-muted-foreground">
                Try a different search or status filter.
              </p>
            </div>
          ) : view === 'table' ? (
            <PrintersTable
              printers={filteredPrinters}
              maintenanceByPrinterId={maintenanceByPrinterId}
              onOpen={(id) => navigate(`/printers/${id}`)}
              onMarkReady={(id) => markReadyMutation.mutate({ id })}
              onEdit={setEditingPrinterId}
              onRemove={handleRemove}
            />
          ) : (
            <PrintersGrid
              printers={filteredPrinters}
              onMarkReady={(id) => markReadyMutation.mutate({ id })}
              onEdit={setEditingPrinterId}
              onRemove={handleRemove}
            />
          )}
        </div>
      )}

      {editingPrinter && (
        <EditPrinterDialog
          printerId={editingPrinter.id ?? ''}
          printerName={editingPrinter.name ?? ''}
          serialNumber={editingPrinter.serialNumber ?? ''}
          currentIpAddress={editingPrinter.ipAddress ?? ''}
          open={editingPrinterId !== null}
          onOpenChange={(open) => {
            if (!open) {
              setEditingPrinterId(null);
            }
          }}
        />
      )}
    </div>
  );
}
