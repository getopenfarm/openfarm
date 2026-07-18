import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useNavigate, useParams } from 'react-router';
import { toast } from 'sonner';
import { BackLink } from '@/components/back-link/BackLink';
import type { PrinterStatus } from '@/components/printer-tile/status';
import { StatusBadge } from '@/components/status-badge/StatusBadge';
import { Skeleton } from '@/components/ui/skeleton';
import type { PrinterDetailResponse } from '@/lib/api/generated/endpoints.schemas';
import {
  getOpenFarmApiEndpointsPrintersGetPrinterDetailEndpointQueryKey,
  getOpenFarmApiEndpointsPrintersListFleetEndpointQueryKey,
  useOpenFarmApiEndpointsPrintersGetPrinterDetailEndpoint,
  useOpenFarmApiEndpointsPrintersMarkPrinterReadyEndpoint,
  useOpenFarmApiEndpointsPrintersRemovePrinterEndpoint,
} from '@/lib/api/generated/printers/printers';
import { AmsSlotGrid } from './components/AmsSlotGrid';
import { CameraView } from './components/CameraView';
import { CurrentJobCard } from './components/CurrentJobCard';
import { EditPrinterDialog } from './components/EditPrinterDialog';
import { FeedUnitsCard } from './components/FeedUnitsCard';
import { PrinterDetailActions } from './components/PrinterDetailActions';
import { RecentActivityCard } from './components/RecentActivityCard';
import { printerStatusToBadge } from './lib/printerStatusBadge';

export function PrinterDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [settingsOpen, setSettingsOpen] = useState(false);
  const detailQuery = useOpenFarmApiEndpointsPrintersGetPrinterDetailEndpoint(
    id ?? '',
  );

  const markReadyMutation =
    useOpenFarmApiEndpointsPrintersMarkPrinterReadyEndpoint({
      mutation: {
        onSuccess: () => {
          toast.success('Printer marked ready.');
          queryClient.invalidateQueries({
            queryKey:
              getOpenFarmApiEndpointsPrintersGetPrinterDetailEndpointQueryKey(
                id ?? '',
              ),
          });
        },
        onError: () => {
          toast.error('Could not mark printer ready.');
        },
      },
    });

  const removeMutation = useOpenFarmApiEndpointsPrintersRemovePrinterEndpoint({
    mutation: {
      onSuccess: () => {
        toast.success('Printer removed.');
        queryClient.invalidateQueries({
          queryKey: getOpenFarmApiEndpointsPrintersListFleetEndpointQueryKey(),
        });
        navigate('/printers');
      },
      onError: () => {
        toast.error('Could not remove printer.');
      },
    },
  });

  function handleRemove(printerId: string, printerName: string) {
    const confirmed = window.confirm(
      `Remove ${printerName}? This can't be undone. Removal isn't blocked by its current print status.`,
    );
    if (!confirmed) {
      return;
    }
    removeMutation.mutate({ id: printerId });
  }

  const printer = detailQuery.data?.data as PrinterDetailResponse | undefined;

  return (
    <div className="flex flex-col gap-6">
      <BackLink to="/printers">Back to fleet</BackLink>

      {detailQuery.isLoading ? (
        <Skeleton className="h-64 w-full" />
      ) : !printer ? (
        <p className="text-sm text-muted-foreground">Printer not found.</p>
      ) : (
        <>
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div>
              <div className="flex items-center gap-3">
                <h2 className="font-heading text-2xl font-semibold">
                  {printer.name}
                </h2>
                {(() => {
                  const { kind, label } = printerStatusToBadge(
                    (printer.status ?? 'Idle') as PrinterStatus,
                    printer.isReachable ?? false,
                  );
                  return <StatusBadge kind={kind} label={label} />;
                })()}
              </div>
              <p className="font-mono text-sm text-muted-foreground">
                {printer.brand} {printer.modelCode} · {printer.ipAddress}
              </p>
            </div>
            <PrinterDetailActions
              status={(printer.status ?? 'Idle') as PrinterStatus}
              isReachable={printer.isReachable ?? false}
              isMarkReadyPending={markReadyMutation.isPending}
              isRemovePending={removeMutation.isPending}
              onMarkReady={() =>
                markReadyMutation.mutate({ id: printer.id ?? '' })
              }
              onOpenSettings={() => setSettingsOpen(true)}
              onRemove={() =>
                handleRemove(printer.id ?? '', printer.name ?? '')
              }
            />
          </div>
          <div className="grid grid-cols-1 gap-4 lg:grid-cols-[1.6fr_1fr]">
            <div className="flex flex-col gap-4">
              <CameraView
                printerId={printer.id ?? ''}
                telemetry={printer.telemetry ?? {}}
              />
              <CurrentJobCard telemetry={printer.telemetry ?? {}} />
            </div>
            <div className="flex flex-col gap-4">
              <AmsSlotGrid amsUnits={printer.amsUnits ?? []} />
              <FeedUnitsCard printerId={printer.id ?? ''} />
              <RecentActivityCard printerId={printer.id ?? ''} />
            </div>
          </div>
          <EditPrinterDialog
            printerId={printer.id ?? ''}
            printerName={printer.name ?? ''}
            brand={printer.brand ?? ''}
            modelCode={printer.modelCode ?? ''}
            serialNumber={printer.serialNumber ?? ''}
            currentIpAddress={printer.ipAddress ?? ''}
            open={settingsOpen}
            onOpenChange={setSettingsOpen}
          />
        </>
      )}
    </div>
  );
}
