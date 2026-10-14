import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useNavigate, useParams } from 'react-router';
import { toast } from 'sonner';
import { BackLink } from '@/components/back-link/BackLink';
import { ConfirmDialog } from '@/components/confirm-dialog/ConfirmDialog';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { PageHeader } from '@/components/page-header/PageHeader';
import type { PrinterStatus } from '@/components/printer-tile/status';
import { StatusBadge } from '@/components/status-badge/StatusBadge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import type { PrinterDetailResponse } from '@/lib/api/generated/endpoints.schemas';
import {
  getOpenFarmApiEndpointsPrintersListFleetEndpointQueryKey,
  useOpenFarmApiEndpointsPrintersGetPrinterDetailEndpoint,
  useOpenFarmApiEndpointsPrintersRemovePrinterEndpoint,
} from '@/lib/api/generated/printers/printers';
import { CameraView } from './components/CameraView';
import { CurrentJobCard } from './components/CurrentJobCard';
import { FeedUnitsCard } from './components/FeedUnitsCard';
import { PrinterDetailActions } from './components/PrinterDetailActions';
import { PrinterReportedStatus } from './components/PrinterReportedStatus';
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

  const backLink = (
    <BackLink to="/printers" className="mb-1">
      Back to fleet
    </BackLink>
  );
  const queryError: unknown = detailQuery.error;
  const isNotFound =
    !printer &&
    (!detailQuery.isError ||
      (queryError instanceof Response && queryError.status === 404));

  return (
    <div className="flex flex-col gap-6">
      {markReadyMutation.confirmationDialog}

      {detailQuery.isLoading ? (
        <>
          <PageHeader above={backLink} title="Printer" />
          <Skeleton className="h-64 w-full" />
        </>
      ) : !printer ? (
        <>
          <PageHeader above={backLink} title="Printer" />
          {isNotFound ? (
            <EmptyState
              title="Printer not found"
              description="It may have been removed, or the link is out of date."
              action={
                <Button variant="outline" onClick={() => navigate('/printers')}>
                  Back to fleet
                </Button>
              }
            />
          ) : (
            <ErrorState
              title="Could not load this printer"
              onRetry={() => detailQuery.refetch()}
            />
          )}
        </>
      ) : (
        <>
          <PageHeader
            above={backLink}
            title={
              <span className="flex items-center gap-3">
                {printer.name}
                {(() => {
                  const { kind, label } = printerStatusToBadge(
                    (printer.status ?? 'Idle') as PrinterStatus,
                    printer.isReachable ?? false,
                  );
                  return <StatusBadge kind={kind} label={label} />;
                })()}
              </span>
            }
            description={
              <span className="font-mono">
                {printer.brand} {printer.modelCode} · {printer.ipAddress}
              </span>
            }
            action={
              <PrinterDetailActions
                status={(printer.status ?? 'Idle') as PrinterStatus}
                isReachable={printer.isReachable ?? false}
                isMarkReadyPending={markReadyMutation.isPending}
                isRemovePending={removeMutation.isPending}
                onMarkReady={() =>
                  markReadyMutation.mutate({ id: printer.id ?? '' })
                }
                onEdit={() => navigate(`/printers/${printer.id}/edit`)}
                onRemove={() => setConfirmRemoveOpen(true)}
              />
            }
          />
          <PrinterReportedStatus
            telemetry={printer.telemetry}
            isReachable={printer.isReachable ?? false}
          />
          <div className="grid grid-cols-1 gap-4 lg:grid-cols-[1.6fr_1fr]">
            <div className="flex flex-col gap-4">
              <CameraView
                isReachable={printer.isReachable ?? false}
                modelCode={printer.modelCode}
                printerId={printer.id ?? ''}
                telemetry={printer.telemetry ?? {}}
              />
              <CurrentJobCard telemetry={printer.telemetry ?? {}} />
            </div>
            <div className="flex flex-col gap-4">
              <FeedUnitsCard
                printerId={printer.id ?? ''}
                externalSpoolTray={printer.externalSpoolTray}
                amsUnits={printer.amsUnits ?? []}
                isReachable={printer.isReachable ?? false}
              />
              <Card className="gap-0 py-0">
                <CardHeader className="border-b px-[18px] py-3.5">
                  <CardTitle className="text-sm font-semibold">
                    Machine &amp; process profile
                  </CardTitle>
                </CardHeader>
                <CardContent className="flex flex-col gap-2 px-[18px] py-3.5">
                  <p className="text-sm">
                    Reported nozzle:{' '}
                    <span className="text-muted-foreground">
                      {printer.telemetry?.nozzleDiameterMm
                        ? `${printer.telemetry.nozzleDiameterMm} mm${printer.telemetry.nozzleConfirmed ? '' : ' · awaiting confirmation'}`
                        : 'Waiting for printer telemetry'}
                    </span>
                  </p>
                  {printer.machineProfileIssue && (
                    <p className="text-sm text-destructive">
                      {printer.machineProfileIssue}
                    </p>
                  )}
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
                      Select a machine profile matching the reported nozzle in
                      settings before using this printer for production.
                    </p>
                  )}
                </CardContent>
              </Card>
              <RecentActivityCard printerId={printer.id ?? ''} />
            </div>
          </div>
          <ConfirmDialog
            open={confirmRemoveOpen}
            onOpenChange={setConfirmRemoveOpen}
            title="Remove printer?"
            description={`Remove ${printer.name}? This can't be undone. Removal isn't blocked by its current print status.`}
            confirmLabel="Remove"
            destructive
            isPending={removeMutation.isPending}
            onConfirm={() => handleConfirmRemove(printer.id ?? '')}
          />
        </>
      )}
    </div>
  );
}
