import { useQueryClient } from '@tanstack/react-query';
import { VideoOffIcon } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { Badge } from '@/components/ui/badge';
import { getOpenFarmApiEndpointsPrintersGetPrinterCameraFrameEndpointUrl } from '@/lib/api/generated/printers/printers';
import { authenticatedFetch } from '@/lib/auth/session';
import { useAuthorizedQuery as useQuery } from '@/lib/auth/useAuthorizedQuery';
import { formatDateTime } from '@/lib/formatDateTime';
import { cn } from '@/lib/utils';

const FRESHNESS_TIMEOUT_MS = 5000;
/** The server stores at most one frame a second, so polling faster only adds 304s. */
const POLL_INTERVAL_MS = 1000;

export interface CameraFrame {
  /** A data: URL, so a frame stays valid for as long as anything still shows it. */
  src: string;
  receivedAtUtc: number;
  etag: string | null;
}

/** Keyed by the endpoint path so useAuthorizedQuery gates it on printers:list. */
export function cameraFrameQueryKey(printerId: string) {
  return [
    getOpenFarmApiEndpointsPrintersGetPrinterCameraFrameEndpointUrl(printerId),
  ] as const;
}

function readAsDataUrl(blob: Blob) {
  return new Promise<string>((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(reader.result as string);
    reader.onerror = () => reject(reader.error);
    reader.readAsDataURL(blob);
  });
}

/**
 * One poll of the latest frame. Sends the previous ETag, so an unchanged frame is a bodyless 304 that keeps the
 * previous data (same reference: no re-render). Each request also keeps the printer's camera running server-side.
 */
export async function fetchCameraFrame(
  printerId: string,
  previous: CameraFrame | null,
  signal?: AbortSignal,
): Promise<CameraFrame | null> {
  const response = await authenticatedFetch(
    getOpenFarmApiEndpointsPrintersGetPrinterCameraFrameEndpointUrl(printerId),
    {
      signal,
      headers: previous?.etag ? { 'If-None-Match': previous.etag } : {},
    },
  );
  if (response.status === 304) return previous;
  if (response.status === 204) return previous ?? null;
  if (!response.ok) throw response;
  const etag = response.headers.get('ETag');
  if (previous && etag && etag === previous.etag) return previous;
  const receivedAtUtc = Date.parse(
    response.headers.get('X-Frame-Received-At') ?? '',
  );
  if (!Number.isFinite(receivedAtUtc)) return previous;
  return {
    src: await readAsDataUrl(await response.blob()),
    receivedAtUtc,
    etag,
  };
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
  const client = useQueryClient();
  const queryKey = cameraFrameQueryKey(printerId);
  // Polls only while on screen in a visible tab (TanStack pauses intervals in background tabs); an offline printer
  // loads its last frame once.
  const { data } = useQuery<CameraFrame | null>({
    queryKey,
    enabled: visible && supported && Boolean(printerId),
    queryFn: ({ signal }) =>
      fetchCameraFrame(
        printerId,
        client.getQueryData<CameraFrame | null>(queryKey) ?? null,
        signal,
      ),
    refetchInterval: isReachable ? POLL_INTERVAL_MS : false,
    refetchOnWindowFocus: false,
  });
  const [now, setNow] = useState(Date.now);
  useEffect(() => {
    if (!data || !visible) return;
    setNow(Date.now());
    const interval = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(interval);
  }, [data, visible]);
  const [lastGood, setLastGood] = useState<
    (CameraFrame & { printerId: string }) | null
  >(null);
  const [failedFrame, setFailedFrame] = useState<string | null>(null);
  const invalidFrame = Boolean(data && failedFrame === data.src);
  const fallback = lastGood?.printerId === printerId ? lastGood : null;
  const displayed = invalidFrame ? fallback : data;
  const fresh = Boolean(
    data &&
      now - data.receivedAtUtc < FRESHNESS_TIMEOUT_MS &&
      Date.now() - data.receivedAtUtc < FRESHNESS_TIMEOUT_MS,
  );
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
          onError={() => setFailedFrame(displayed.src)}
          src={displayed.src}
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
