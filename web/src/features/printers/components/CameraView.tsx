import { useQuery } from '@tanstack/react-query';
import { VideoOffIcon } from 'lucide-react';
import { useEffect, useState } from 'react';
import type { PrinterDetailTelemetryResponse } from '@/lib/api/generated/endpoints.schemas';
import {
  type CameraFrameQueryData,
  cameraFrameQueryKey,
} from '../hooks/useFarmHub';

// No CameraFrameReceived event within this window means the feed is treated as unavailable
// (FR-009's graceful-degradation requirement) — covers both CAMERA_RTSP printers (unimplemented
// this phase) and a CAMERA_IMAGE printer that's simply offline or mid-reconnect.
const FRESHNESS_TIMEOUT_MS = 5000;

export interface CameraViewProps {
  printerId: string;
  telemetry: PrinterDetailTelemetryResponse;
}

function formatTemp(value: number | null | undefined): string {
  return value == null ? '—' : `${value}°C`;
}

function formatProgress(value: number | null | undefined): string {
  return value == null ? '—' : `${value}%`;
}

export function CameraView({ printerId, telemetry }: CameraViewProps) {
  const { data } = useQuery<CameraFrameQueryData | null>({
    queryKey: cameraFrameQueryKey(printerId),
    queryFn: () => null,
    enabled: false,
    initialData: null,
    staleTime: Infinity,
  });
  const [isFresh, setIsFresh] = useState(false);

  useEffect(() => {
    if (!data) {
      setIsFresh(false);
      return;
    }

    setIsFresh(true);
    const timeout = setTimeout(() => setIsFresh(false), FRESHNESS_TIMEOUT_MS);
    return () => clearTimeout(timeout);
  }, [data]);

  const hasFrame = isFresh && data != null;
  const progressText = formatProgress(telemetry.printProgressPercent);

  return (
    <div className="overflow-hidden rounded-xl bg-card ring-1 ring-foreground/10">
      <div className="relative h-64 bg-[radial-gradient(120%_120%_at_50%_0%,#334155,#0f172a)]">
        <div className="absolute inset-0 bg-[linear-gradient(rgba(148,163,184,.09)_1px,transparent_1px),linear-gradient(90deg,rgba(148,163,184,.09)_1px,transparent_1px)] bg-[length:28px_28px]" />
        {hasFrame ? (
          <>
            <img
              src={`data:image/jpeg;base64,${data.frameJpegBase64}`}
              alt="Live printer camera feed"
              className="absolute inset-0 size-full object-cover"
            />
            <div className="absolute top-3 left-3 inline-flex items-center gap-1.5 rounded-full bg-slate-900/55 px-2.5 py-1">
              <span className="size-[7px] rounded-full bg-red-500 ring-3 ring-red-500/25" />
              <span className="text-[11px] font-semibold tracking-wider text-white">
                LIVE
              </span>
            </div>
            {telemetry.printProgressPercent != null && (
              <div className="absolute inset-x-0 bottom-0 flex items-end justify-between gap-2.5 bg-[linear-gradient(transparent,rgba(15,23,42,.78))] px-4 pt-6 pb-3">
                <span className="truncate text-[13px] text-slate-200">
                  {telemetry.currentStageLabel ?? 'Printing'}
                </span>
                <span className="shrink-0 font-mono text-[13px] font-semibold text-white">
                  {progressText}
                </span>
              </div>
            )}
          </>
        ) : (
          <div className="absolute inset-0 flex flex-col items-center justify-center gap-2.5">
            <VideoOffIcon className="size-7 text-slate-400/85" />
            <span className="text-[13px] text-slate-400/85">
              Camera unavailable
            </span>
          </div>
        )}
      </div>
      <div className="grid grid-cols-2 border-t sm:grid-cols-4">
        <div className="border-r px-4 py-3.5">
          <p className="text-xs text-muted-foreground">Nozzle</p>
          <p className="mt-1 font-mono text-lg font-semibold">
            {formatTemp(telemetry.nozzleTempC)}
          </p>
        </div>
        <div className="border-r px-4 py-3.5">
          <p className="text-xs text-muted-foreground">Bed</p>
          <p className="mt-1 font-mono text-lg font-semibold">
            {formatTemp(telemetry.bedTempC)}
          </p>
        </div>
        <div className="border-r px-4 py-3.5">
          <p className="text-xs text-muted-foreground">Progress</p>
          <p className="mt-1 font-mono text-lg font-semibold text-status-printing">
            {progressText}
          </p>
        </div>
        <div className="px-4 py-3.5">
          <p className="text-xs text-muted-foreground">Stage</p>
          <p className="mt-1 text-lg font-semibold">
            {telemetry.currentStageLabel ?? '—'}
          </p>
        </div>
      </div>
    </div>
  );
}
