import { useQueryClient } from '@tanstack/react-query';
import {
  CheckCircle2Icon,
  PencilIcon,
  PrinterIcon,
  Trash2Icon,
} from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router';
import { toast } from 'sonner';
import { PrinterTile } from '@/components/printer-tile/PrinterTile';
import type { PrinterStatus } from '@/components/printer-tile/status';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import type { ListFleetResponse } from '@/lib/api/generated/endpoints.schemas';
import {
  getOpenFarmApiEndpointsPrintersListFleetEndpointQueryKey,
  useOpenFarmApiEndpointsPrintersListFleetEndpoint,
  useOpenFarmApiEndpointsPrintersMarkPrinterReadyEndpoint,
  useOpenFarmApiEndpointsPrintersRemovePrinterEndpoint,
} from '@/lib/api/generated/printers/printers';
import { AddPrinterDialog } from './components/AddPrinterDialog';
import { EditPrinterDialog } from './components/EditPrinterDialog';
import { useFarmHub } from './hooks/useFarmHub';

// Telemetry (temps, filament color, ETA) lands on the printer detail page in Phase 4 — the
// fleet list only ever exposes status/progress (contracts/printers-api.md's ListFleet shape).
const PLACEHOLDER_FILAMENT_COLOR = '#8892a6';

export function PrintersPage() {
  useFarmHub();
  const queryClient = useQueryClient();
  const fleetQuery = useOpenFarmApiEndpointsPrintersListFleetEndpoint();
  const [editingPrinterId, setEditingPrinterId] = useState<string | null>(null);

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
      ) : printers.length === 0 ? (
        <div className="flex flex-col items-center gap-2 rounded-xl border border-dashed p-12 text-center">
          <PrinterIcon className="size-8 text-muted-foreground" />
          <p className="text-sm text-muted-foreground">
            No printers registered yet. Add one to see its live status here.
          </p>
        </div>
      ) : (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-3">
          {printers.map((printer) => (
            <div key={printer.id} className="group relative">
              <Link to={`/printers/${printer.id}`} className="block">
                <PrinterTile
                  name={printer.name ?? ''}
                  status={(printer.status ?? 'Idle') as PrinterStatus}
                  isReachable={printer.isReachable ?? false}
                  filamentColorHex={PLACEHOLDER_FILAMENT_COLOR}
                  progressPercent={printer.printProgressPercent ?? 0}
                  nozzleTempC={0}
                  bedTempC={0}
                  etaMinutes={null}
                />
              </Link>
              <div className="absolute top-2 right-11 flex gap-1 opacity-0 transition-opacity group-hover:opacity-100">
                {printer.status === 'AwaitingClear' && (
                  <Button
                    variant="ghost"
                    size="icon-sm"
                    aria-label={`Mark ${printer.name} ready`}
                    onClick={() =>
                      markReadyMutation.mutate({ id: printer.id ?? '' })
                    }
                  >
                    <CheckCircle2Icon className="size-4" />
                  </Button>
                )}
                <Button
                  variant="ghost"
                  size="icon-sm"
                  aria-label={`Edit ${printer.name}`}
                  onClick={() => setEditingPrinterId(printer.id ?? null)}
                >
                  <PencilIcon className="size-4" />
                </Button>
                <Button
                  variant="ghost"
                  size="icon-sm"
                  aria-label={`Remove ${printer.name}`}
                  onClick={() =>
                    handleRemove(printer.id ?? '', printer.name ?? '')
                  }
                >
                  <Trash2Icon className="size-4" />
                </Button>
              </div>
            </div>
          ))}
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
