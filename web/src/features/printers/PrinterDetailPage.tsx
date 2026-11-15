import { useQueryClient } from '@tanstack/react-query';
import { SlidersHorizontalIcon } from 'lucide-react';
import { useState } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router';
import { toast } from 'sonner';
import { BackLink } from '@/components/back-link/BackLink';
import { ConfirmDialog } from '@/components/confirm-dialog/ConfirmDialog';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { PermissionButton } from '@/components/PermissionButton';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import type { PrinterStatus } from '@/components/printer-tile/status';
import { StatusBadge } from '@/components/status-badge/StatusBadge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { monitoringStatus } from '@/features/dashboard/lib/monitoring';
import type { PrinterDetailResponse } from '@/lib/api/generated/endpoints.schemas';
import {
  getOpenFarmApiEndpointsPrintersListFleetEndpointQueryKey,
  useOpenFarmApiEndpointsPrintersGetPrinterDetailEndpoint,
  useOpenFarmApiEndpointsPrintersRemovePrinterEndpoint,
} from '@/lib/api/generated/printers/printers';
import { useAllowed } from '@/lib/auth/AccessContext';
import { CameraView } from './components/CameraView';
import { CurrentJobCard } from './components/CurrentJobCard';
import { FeedUnitsCard } from './components/FeedUnitsCard';
import { HardwareCard } from './components/HardwareCard';
import { MaintenanceCard } from './components/MaintenanceCard';
import { PrinterDetailActions } from './components/PrinterDetailActions';
import { PrinterReportedStatus } from './components/PrinterReportedStatus';
import { RecentActivityCard } from './components/RecentActivityCard';
import { SlicerSettingsCard } from './components/SlicerSettingsCard';
import {
  SLICER_SETTINGS_PERMISSION,
  SlicerSettingsDialog,
} from './components/SlicerSettingsDialog';
import { useMarkPrinterNotReady } from './hooks/useMarkPrinterNotReady';
import { useMarkPrinterReady } from './hooks/useMarkPrinterReady';
import { usePrinterSendProgress } from './hooks/usePrinterSendProgress';

export function PrinterDetailPage() {
  const { id } = useParams<{ id: string }>();
  const sending = usePrinterSendProgress(id ?? '');
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [confirmRemoveOpen, setConfirmRemoveOpen] = useState(false);
  const detailQuery = useOpenFarmApiEndpointsPrintersGetPrinterDetailEndpoint(
    id ?? '',
  );

  const markReadyMutation = useMarkPrinterReady();
  const markNotReadyMutation = useMarkPrinterNotReady();
  const canMarkReady = useAllowed('printers:mark-ready');
  const canEditSlicer = useAllowed(SLICER_SETTINGS_PERMISSION);
  // The dialog lives in the URL so links and the Back button reach it: /printers/:id?settings=slicer.
  const [searchParams, setSearchParams] = useSearchParams();
  const slicerOpen = searchParams.get('settings') === 'slicer';
  const setSlicerOpen = (open: boolean) =>
    setSearchParams(
      (current) => {
        const next = new URLSearchParams(current);
        if (open) next.set('settings', 'slicer');
        else next.delete('settings');
        return next;
      },
      { replace: !open },
    );

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
    <Page>
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
              description="Removed or invalid link."
              action={
                <Button variant="outline" onClick={() => navigate('/printers')}>
                  Back to fleet
                </Button>
              }
            />
          ) : (
            <ErrorState
              description="Could not load printer."
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
                  const badge = monitoringStatus(printer, sending);
                  const canClear =
                    printer.isReachable && printer.status === 'AwaitingClear';
                  return (
                    <StatusBadge
                      kind={badge.kind}
                      solid={badge.solid}
                      label={
                        markReadyMutation.isPending
                          ? 'Marking ready…'
                          : badge.label
                      }
                      action={
                        canClear && canMarkReady
                          ? {
                              onClick: () =>
                                markReadyMutation.mutate({
                                  id: printer.id ?? '',
                                }),
                              label: `Mark ${printer.name} ready`,
                              disabled: markReadyMutation.isPending,
                            }
                          : undefined
                      }
                    />
                  );
                })()}
              </span>
            }
            description={
              <span className="font-mono">
                {printer.brand} {printer.modelCode} · {printer.ipAddress}
              </span>
            }
            action={
              <div className="flex gap-2">
                {/* Any status: plates are swapped while a printer prints. Primary while the printer cannot take a job. */}
                <PermissionButton
                  permission={SLICER_SETTINGS_PERMISSION}
                  variant={printer.setupIssues?.length ? 'default' : 'outline'}
                  onClick={() => setSlicerOpen(true)}
                >
                  <SlidersHorizontalIcon data-icon="inline-start" /> Slicer
                  settings
                </PermissionButton>
                <PrinterDetailActions
                  status={(printer.status ?? 'Idle') as PrinterStatus}
                  isReachable={printer.isReachable ?? false}
                  isMarkReadyPending={markReadyMutation.isPending}
                  isMarkNotReadyPending={markNotReadyMutation.isPending}
                  isRemovePending={removeMutation.isPending}
                  onMarkReady={() =>
                    markReadyMutation.mutate({ id: printer.id ?? '' })
                  }
                  onMarkNotReady={() =>
                    markNotReadyMutation.mutate({ id: printer.id ?? '' })
                  }
                  onEdit={() => navigate(`/printers/${printer.id}/edit`)}
                  onRemove={() => setConfirmRemoveOpen(true)}
                />
              </div>
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
                cameraSupported={printer.model?.cameraSupported ?? false}
                printerId={printer.id ?? ''}
                telemetry={printer.telemetry ?? {}}
              />
              <CurrentJobCard telemetry={printer.telemetry ?? {}} />
            </div>
            <div className="flex flex-col gap-4">
              <FeedUnitsCard
                printerId={printer.id ?? ''}
                feedReadings={printer.feedReadings}
                isReachable={printer.isReachable ?? false}
              />
              <HardwareCard
                model={printer.model}
                extruders={printer.telemetry?.extruders}
              />
              <SlicerSettingsCard printer={printer} />
              <MaintenanceCard printerId={printer.id ?? ''} />
              <RecentActivityCard printerId={printer.id ?? ''} />
            </div>
          </div>
          <SlicerSettingsDialog
            printerId={
              slicerOpen && canEditSlicer ? (printer.id ?? null) : null
            }
            onOpenChange={setSlicerOpen}
          />
          <ConfirmDialog
            open={confirmRemoveOpen}
            onOpenChange={setConfirmRemoveOpen}
            title="Remove printer?"
            description={`Remove ${printer.name}? This cannot be undone.`}
            confirmLabel="Remove"
            destructive
            isPending={removeMutation.isPending}
            onConfirm={() => handleConfirmRemove(printer.id ?? '')}
          />
        </>
      )}
    </Page>
  );
}
