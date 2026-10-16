import { useQuery, useQueryClient } from '@tanstack/react-query';
import { VideoOffIcon } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { Badge } from '@/components/ui/badge';
import { openFarmApiEndpointsPrintersGetPrinterCameraSnapshotEndpoint } from '@/lib/api/generated/printers/printers';
import { formatDateTime } from '@/lib/formatDateTime';
import { cn } from '@/lib/utils';
import {
  type CameraFrameQueryData,
  cameraFrameQueryKey,
} from '../hooks/useFarmHub';

const FRESHNESS_TIMEOUT_MS = 5000;

/** X1-family RTSP is not implemented by the current relay (PrinterModelCatalog). */
export function cameraSupported(modelCode?: string) {
  return !['X1', 'X1C', 'X1E'].includes(modelCode?.toUpperCase() ?? '');
}

export function formatCameraAge(ageMs: number) {
  const seconds = Math.max(0, Math.floor(ageMs / 1000));
  if (seconds < 60) return `${seconds}s ago`;
  if (seconds < 3600) return `${Math.floor(seconds / 60)}m ago`;
  if (seconds < 86400) return `${Math.floor(seconds / 3600)}h ago`;
  return `${Math.floor(seconds / 86400)}d ago`;
}

export function CameraSurface({
  printerId,
  name = 'Printer',
  isReachable = true,
  supported = true,
  className,
  compact = false,
}: {
  printerId: string;
  name?: string;
  isReachable?: boolean;
  supported?: boolean;
  className?: string;
  compact?: boolean;
}) {
  const container = useRef<HTMLDivElement>(null);
  const [aspectRatio, setAspectRatio] = useState(16 / 9);
  const [visible, setVisible] = useState(
    typeof IntersectionObserver === 'undefined',
  );
  useEffect(() => {
    if (typeof IntersectionObserver === 'undefined' || !container.current)
      return;
    const observer = new IntersectionObserver(([entry]) =>
      setVisible(entry.isIntersecting),
    );
    observer.observe(container.current);
    return () => observer.disconnect();
  }, []);
  const { data } = useQuery<CameraFrameQueryData | null>({
    queryKey: cameraFrameQueryKey(printerId),
    queryFn: () => null,
    enabled: false,
    initialData: null,
    staleTime: Infinity,
  });
  const client = useQueryClient();
  useQuery({
    queryKey: ['printer-camera-snapshot', printerId],
    enabled: visible && supported && Boolean(printerId),
    queryFn: async ({ signal }) => {
      const response =
        await openFarmApiEndpointsPrintersGetPrinterCameraSnapshotEndpoint(
          printerId,
          {
            signal,
          },
        );
      const snapshot = response.status === 200 ? response.data : null;
      if (snapshot?.frameJpegBase64 && snapshot.receivedAtUtc) {
        const frameJpegBase64 = snapshot.frameJpegBase64;
        const receivedAtUtc = Date.parse(snapshot.receivedAtUtc);
        if (Number.isFinite(receivedAtUtc)) {
          client.setQueryData<CameraFrameQueryData | null>(
            cameraFrameQueryKey(printerId),
            (previous) =>
              previous && previous.receivedAtUtc >= receivedAtUtc
                ? previous
                : {
                    frameJpegBase64,
                    receivedAtUtc,
                    fromSnapshot: true,
                  },
          );
        }
      }
      return snapshot;
    },
    refetchOnWindowFocus: true,
  });
  const [now, setNow] = useState(Date.now);
  useEffect(() => {
    if (!data || !visible) return;
    setNow(Date.now());
    const interval = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(interval);
  }, [data, visible]);
  const [lastGood, setLastGood] = useState<
    (CameraFrameQueryData & { printerId: string }) | null
  >(null);
  const [failedFrame, setFailedFrame] = useState<string | null>(null);
  const invalidFrame = Boolean(data && failedFrame === data.frameJpegBase64);
  const fallback = lastGood?.printerId === printerId ? lastGood : null;
  const displayed = invalidFrame ? fallback : data;
  const fresh = Boolean(
    data &&
      now - data.receivedAtUtc < FRESHNESS_TIMEOUT_MS &&
      Date.now() - data.receivedAtUtc < FRESHNESS_TIMEOUT_MS,
  );
  const live =
    isReachable && supported && fresh && !invalidFrame && !data?.fromSnapshot;
  const label = !isReachable
    ? 'Printer offline'
    : !supported
      ? 'Camera not supported yet'
      : invalidFrame
        ? 'Camera unavailable · Invalid frame'
        : data
          ? 'Camera stale · Waiting for frames'
          : 'Camera unavailable';
  const displayLabel = !compact
    ? label
    : !isReachable
      ? 'Offline'
      : !supported
        ? 'Camera unsupported'
        : invalidFrame
          ? 'Invalid frame'
          : data
            ? 'Camera stale'
            : 'Camera unavailable';
  return (
    <div
      ref={container}
      style={{ aspectRatio }}
      className={cn(
        'relative flex items-center justify-center overflow-hidden bg-muted',
        className,
      )}
    >
      {visible && displayed && supported ? (
        <img
          onLoad={({ currentTarget }) => {
            setLastGood({ ...displayed, printerId });
            if (
              currentTarget.naturalWidth > 0 &&
              currentTarget.naturalHeight > 0
            )
              setAspectRatio(
                currentTarget.naturalWidth / currentTarget.naturalHeight,
              );
          }}
          onError={() => setFailedFrame(displayed.frameJpegBase64)}
          src={`data:image/jpeg;base64,${displayed.frameJpegBase64}`}
          alt={`${name} camera${live ? ' live view' : ' last received frame'}`}
          className="absolute inset-0 size-full object-contain"
        />
      ) : null}
      {displayed && supported ? (
        <Badge
          variant="secondary"
          title={`Last frame received ${formatDateTime(displayed.receivedAtUtc)} · ${live ? 'Live' : label}`}
          aria-label={
            live
              ? 'Live camera'
              : `Last camera frame ${formatCameraAge(now - displayed.receivedAtUtc)}. ${label}. Received ${formatDateTime(displayed.receivedAtUtc)}`
          }
          className={cn(
            'absolute top-3 left-3 shadow-sm',
            compact && 'top-1.5 left-1.5',
          )}
        >
          <span
            className={cn(
              'size-1.5 rounded-full',
              live ? 'bg-kind-green' : 'bg-muted-foreground',
            )}
          />
          {live ? 'Live' : formatCameraAge(now - displayed.receivedAtUtc)}
        </Badge>
      ) : (
        <div
          title={label}
          className={cn(
            'relative flex max-w-full flex-col items-center gap-2 rounded-lg bg-muted/90 px-4 py-3 text-muted-foreground',
            compact && 'gap-1 px-2 py-2 text-center',
          )}
        >
          <VideoOffIcon
            className={compact ? 'size-4' : 'size-6'}
            aria-hidden="true"
          />
          <span className="text-xs">{displayLabel}</span>
        </div>
      )}
    </div>
  );
}
