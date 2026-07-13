import { useQuery } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
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
}

export function CameraView({ printerId }: CameraViewProps) {
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

  return (
    <Card>
      <CardHeader>
        <CardTitle>Camera</CardTitle>
      </CardHeader>
      <CardContent>
        {isFresh && data ? (
          <img
            src={`data:image/jpeg;base64,${data.frameJpegBase64}`}
            alt="Live printer camera feed"
            className="aspect-video w-full rounded-lg object-cover"
          />
        ) : (
          <div className="flex aspect-video w-full items-center justify-center rounded-lg border border-dashed">
            <p className="text-sm text-muted-foreground">Camera unavailable.</p>
          </div>
        )}
      </CardContent>
    </Card>
  );
}
