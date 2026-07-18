import { PrinterIcon } from 'lucide-react';
import { memo } from 'react';
import { CheckerboardThumbnail } from '@/components/checkerboard-thumbnail/CheckerboardThumbnail';
import { StatusBadge } from '@/components/status-badge/StatusBadge';
import { Card, CardContent } from '@/components/ui/card';
import { Progress } from '@/components/ui/progress';
import { printerStatusToBadge } from '@/features/printers/lib/printerStatusBadge';
import { cn } from '@/lib/utils';
import type { PrinterStatus } from './status';

export interface PrinterTileProps {
  name: string;
  status: PrinterStatus;
  isReachable: boolean;
  filamentColorHex: string | null;
  progressPercent: number;
  nozzleTempC: number | null | undefined;
  bedTempC: number | null | undefined;
  etaMinutes: number | null;
  jobName: string | null;
  stageLabel: string | null;
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
  jobName,
  stageLabel,
}: PrinterTileProps) {
  const badge = printerStatusToBadge(status, isReachable);
  const isPrinting = isReachable && status === 'Printing';
  // A finished print still sitting on the bed keeps its name until it's cleared.
  const hasJob = isReachable && (isPrinting || status === 'AwaitingClear');

  return (
    <Card className="gap-0 overflow-hidden py-0">
      <div className="relative">
        <CheckerboardThumbnail
          icon={PrinterIcon}
          label={isPrinting ? `${progressPercent}%` : undefined}
          className={cn(
            'aspect-[16/9] w-full rounded-none',
            !isReachable && 'opacity-50 grayscale',
          )}
        />
        <StatusBadge
          kind={badge.kind}
          label={badge.label}
          className="absolute top-2 left-2 bg-background/85 backdrop-blur-sm"
        />
      </div>

      <CardContent className="flex flex-col gap-3 p-[18px]">
        <div className="flex min-w-0 items-center gap-2">
          <span
            className="size-3 shrink-0 rounded-full ring-1 ring-foreground/15"
            style={{ backgroundColor: filamentColorHex ?? 'transparent' }}
            aria-hidden="true"
          />
          <span className="min-w-0 flex-1 truncate font-medium">{name}</span>
        </div>

        <p className="truncate text-sm text-muted-foreground">
          {hasJob && jobName ? (
            <>
              <span className="text-foreground">{jobName}</span>
              {isPrinting && stageLabel ? ` · ${stageLabel}` : null}
            </>
          ) : status === 'AwaitingClear' && isReachable ? (
            'Ready to clear'
          ) : (
            'No active print'
          )}
        </p>

        {isPrinting && (
          <div className="flex items-center gap-3">
            <Progress
              value={progressPercent}
              className="h-1.5 flex-1"
              indicatorClassName="bg-status-printing"
            />
            <span className="shrink-0 font-mono text-xs font-medium text-status-printing">
              {progressPercent}%
            </span>
          </div>
        )}

        <div className="flex items-center justify-between font-mono text-xs text-muted-foreground">
          <span>
            {formatTemp(nozzleTempC)} / {formatTemp(bedTempC)}
          </span>
          {isPrinting && <span>ETA {formatEta(etaMinutes)}</span>}
        </div>
      </CardContent>
    </Card>
  );
});
