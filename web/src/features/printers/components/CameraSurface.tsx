import { useQuery } from '@tanstack/react-query';
import { VideoOffIcon } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
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
  const [now, setNow] = useState(Date.now);
  useEffect(() => {
    setNow(Date.now());
    if (!data) return;
    const remaining = data.receivedAtUtc + FRESHNESS_TIMEOUT_MS - Date.now();
    if (remaining <= 0) return;
    const timeout = setTimeout(() => setNow(Date.now()), remaining);
    return () => clearTimeout(timeout);
  }, [data]);
  const fresh = Boolean(
    data &&
      now - data.receivedAtUtc < FRESHNESS_TIMEOUT_MS &&
      Date.now() - data.receivedAtUtc < FRESHNESS_TIMEOUT_MS,
  );
  const [failedFrame, setFailedFrame] = useState<string | null>(null);
  const invalidFrame = Boolean(data && failedFrame === data.frameJpegBase64);
  const live = isReachable && supported && fresh && !invalidFrame;
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
      {visible && data && supported && !invalidFrame ? (
        <img
          onLoad={({ currentTarget }) => {
            if (
              currentTarget.naturalWidth > 0 &&
              currentTarget.naturalHeight > 0
            )
              setAspectRatio(
                currentTarget.naturalWidth / currentTarget.naturalHeight,
              );
          }}
          onError={() => setFailedFrame(data.frameJpegBase64)}
          src={`data:image/jpeg;base64,${data.frameJpegBase64}`}
          alt={`${name} camera${live ? ' live view' : ' last received frame'}`}
          className={cn(
            'absolute inset-0 size-full object-contain',
            !live && 'opacity-30 grayscale',
          )}
        />
      ) : null}
      {live ? (
        <span
          className={cn(
            'absolute top-3 left-3 flex items-center gap-1.5 rounded-full bg-background/90 px-2.5 py-1 text-xs font-medium text-foreground shadow-sm',
            compact && 'top-1.5 left-1.5 gap-1 px-1.5 py-0.5',
          )}
        >
          <span className="size-1.5 rounded-full bg-kind-green" />
          Live
        </span>
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
