import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useNavigate, useParams } from 'react-router';
import { toast } from 'sonner';
import { BackLink } from '@/components/back-link/BackLink';
import { ConfirmDialog } from '@/components/confirm-dialog/ConfirmDialog';
import type { PrinterStatus } from '@/components/printer-tile/status';
import { StatusBadge } from '@/components/status-badge/StatusBadge';
import { Skeleton } from '@/components/ui/skeleton';
import type { PrinterDetailResponse } from '@/lib/api/generated/endpoints.schemas';
import {
  getOpenFarmApiEndpointsPrintersListFleetEndpointQueryKey,
  useOpenFarmApiEndpointsPrintersGetPrinterDetailEndpoint,
  useOpenFarmApiEndpointsPrintersRemovePrinterEndpoint,
} from '@/lib/api/generated/printers/printers';
import { AmsSlotGrid } from './components/AmsSlotGrid';
import { CameraView } from './components/CameraView';
import { CurrentJobCard } from './components/CurrentJobCard';
import { FeedUnitsCard } from './components/FeedUnitsCard';
import { PrinterDetailActions } from './components/PrinterDetailActions';
import { RecentActivityCard } from './components/RecentActivityCard';
import { useMarkPrinterReady } from './hooks/useMarkPrinterReady';
import { printerStatusToBadge } from './lib/printerStatusBadge';

export function PrinterDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [confirmRemoveOpen, setConfirmRemoveOpen] = useState(false);
  const detailQuery = useOpenFarmApiEndpointsPrintersGetPrinterDetailEndpoint(
    id ?? '',
  );

  const markReadyMutation = useMarkPrinterReady();

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

  function handleConfirmRemove(printerId: string) {
    removeMutation.mutate({ id: printerId });
  }

  const printer = detailQuery.data?.data as PrinterDetailResponse | undefined;

  return (
    <div className="flex flex-col gap-6">
      {markReadyMutation.confirmationDialog}
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
              onOpenSettings={() => navigate(`/printers/${printer.id}/edit`)}
              onRemove={() => setConfirmRemoveOpen(true)}
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
              <div className="flex flex-col gap-0 rounded-lg border">
                <div className="border-b px-[18px] py-3.5">
                  <h3 className="text-sm font-semibold">
                    Machine &amp; process profile
                  </h3>
                </div>
                <div className="flex flex-col gap-2 px-[18px] py-3.5">
                  <p className="text-sm">
                    Machine:{' '}
                    <span className="text-muted-foreground">
                      {printer.machineProfileName ?? 'Not linked'}
                    </span>
                  </p>
                  <p className="text-sm">
                    Process:{' '}
                    <span className="text-muted-foreground">
                      {printer.processProfileName ?? 'Not linked'}
                    </span>
                  </p>
                  <p className="text-sm">
                    Build plate:{' '}
                    <span className="text-muted-foreground">
                      {printer.buildPlateTypeLabel ?? 'Not selected'}
                    </span>
                  </p>
                  {!printer.machineProfileId && (
                    <p className="text-xs text-muted-foreground">
                      No profile linked — this printer won't count toward the
                      farm's auto-detected nozzle diameter, so uploads of files
                      that need slicing (STL, unsliced 3MF) may be rejected if
                      no other printer provides one. Edit via settings.
                    </p>
                  )}
                </div>
              </div>
              <FeedUnitsCard printerId={printer.id ?? ''} />
              <RecentActivityCard printerId={printer.id ?? ''} />
            </div>
          </div>
          <ConfirmDialog
            open={confirmRemoveOpen}
            onOpenChange={setConfirmRemoveOpen}
            title="Remove printer?"
            description={`Remove ${printer.name}? This can't be undone. Removal isn't blocked by its current print status.`}
            confirmLabel="Remove"
            onConfirm={() => handleConfirmRemove(printer.id ?? '')}
          />
        </>
      )}
    </div>
  );
}
