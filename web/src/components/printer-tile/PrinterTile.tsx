import { memo } from 'react';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { LayerStackProgress } from './LayerStackProgress';
import { OFFLINE_META, type PrinterStatus, STATUS_META } from './status';

export interface PrinterTileProps {
  name: string;
  status: PrinterStatus;
  isReachable: boolean;
  filamentColorHex: string;
  progressPercent: number;
  nozzleTempC: number | null | undefined;
  bedTempC: number | null | undefined;
  etaMinutes: number | null;
}

function formatEta(etaMinutes: number | null): string {
  if (etaMinutes === null) return '—';
  const hours = Math.floor(etaMinutes / 60);
  const minutes = etaMinutes % 60;
  return hours > 0 ? `${hours}h ${minutes}m` : `${minutes}m`;
}

function formatTemp(value: number | null | undefined): string {
  return value == null ? '—' : `${value}°C`;
}

export const PrinterTile = memo(function PrinterTile({
  name,
  status,
  isReachable,
  filamentColorHex,
  progressPercent,
  nozzleTempC,
  bedTempC,
  etaMinutes,
}: PrinterTileProps) {
  const meta = STATUS_META[status];

  return (
    <Card>
      <CardHeader className="flex-row items-center justify-between gap-2 space-y-0">
        <div className="flex min-w-0 items-center gap-2">
          <span
            className="size-3 shrink-0 rounded-full ring-1 ring-foreground/15"
            style={{ backgroundColor: filamentColorHex }}
            aria-hidden="true"
          />
          <CardTitle className="truncate">{name}</CardTitle>
        </div>
        <div className="flex shrink-0 items-center gap-2">
          {!isReachable && (
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
            className={`flex items-center gap-1.5 text-xs font-medium ${meta.textClass}`}
          >
            <span className={`size-1.5 rounded-full ${meta.dotClass}`} />
            {meta.label}
          </span>
        </div>
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        <LayerStackProgress status={status} progressPercent={progressPercent} />
        <div className="flex items-center justify-between font-mono text-xs text-muted-foreground">
          <span>
            {formatTemp(nozzleTempC)} / {formatTemp(bedTempC)}
          </span>
          <span>
            {progressPercent}% · ETA {formatEta(etaMinutes)}
          </span>
        </div>
      </CardContent>
    </Card>
  );
});
