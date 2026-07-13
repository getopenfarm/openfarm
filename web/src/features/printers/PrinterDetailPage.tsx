import { useQueryClient } from '@tanstack/react-query';
import { ArrowLeftIcon, CheckCircle2Icon } from 'lucide-react';
import { Link, useParams } from 'react-router';
import { toast } from 'sonner';
import {
  OFFLINE_META,
  type PrinterStatus,
  STATUS_META,
} from '@/components/printer-tile/status';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import type { PrinterDetailResponse } from '@/lib/api/generated/endpoints.schemas';
import {
  getOpenFarmApiEndpointsPrintersGetPrinterDetailEndpointQueryKey,
  useOpenFarmApiEndpointsPrintersGetPrinterDetailEndpoint,
  useOpenFarmApiEndpointsPrintersMarkPrinterReadyEndpoint,
} from '@/lib/api/generated/printers/printers';
import { AmsSlotGrid } from './components/AmsSlotGrid';
import { CameraView } from './components/CameraView';
import { TelemetryPanel } from './components/TelemetryPanel';
import { useFarmHub } from './hooks/useFarmHub';

export function PrinterDetailPage() {
  useFarmHub();
  const { id } = useParams<{ id: string }>();
  const queryClient = useQueryClient();
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

  const printer = detailQuery.data?.data as PrinterDetailResponse | undefined;

  return (
    <div className="flex flex-col gap-6">
      <Link
        to="/printers"
        className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground"
      >
        <ArrowLeftIcon className="size-4" /> Back to fleet
      </Link>

      {detailQuery.isLoading ? (
        <Skeleton className="h-64 w-full" />
      ) : !printer ? (
        <p className="text-sm text-muted-foreground">Printer not found.</p>
      ) : (
        <>
          <div>
            <div className="flex items-center gap-3">
              <h2 className="font-heading text-2xl font-semibold">
                {printer.name}
              </h2>
              {!printer.isReachable && (
                <span
                  className={`flex items-center gap-1.5 text-xs font-medium ${OFFLINE_META.textClass}`}
                >
                  <span
                    className={`size-1.5 rounded-full ${OFFLINE_META.dotClass}`}
                  />
                  {OFFLINE_META.label}
                </span>
              )}
              <span
                className={`flex items-center gap-1.5 text-xs font-medium ${STATUS_META[(printer.status ?? 'Idle') as PrinterStatus].textClass}`}
              >
                <span
                  className={`size-1.5 rounded-full ${STATUS_META[(printer.status ?? 'Idle') as PrinterStatus].dotClass}`}
                />
                {STATUS_META[(printer.status ?? 'Idle') as PrinterStatus].label}
              </span>
              {printer.status === 'AwaitingClear' && (
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() =>
                    markReadyMutation.mutate({ id: printer.id ?? '' })
                  }
                >
                  <CheckCircle2Icon className="size-4" /> Mark ready
                </Button>
              )}
            </div>
            <p className="text-sm text-muted-foreground">
              {printer.brand} {printer.modelCode} · {printer.ipAddress}
            </p>
          </div>
          <CameraView printerId={printer.id ?? ''} />
          <TelemetryPanel telemetry={printer.telemetry ?? {}} />
          <AmsSlotGrid amsUnits={printer.amsUnits ?? []} />
        </>
      )}
    </div>
  );
}
