import { PrinterIcon } from 'lucide-react';
import { memo } from 'react';
import { CheckerboardThumbnail } from '@/components/checkerboard-thumbnail/CheckerboardThumbnail';
import { StatusBadge } from '@/components/status-badge/StatusBadge';
import { Card, CardContent } from '@/components/ui/card';
import { Progress } from '@/components/ui/progress';
import { printerStatusToBadge } from '@/features/printers/lib/printerStatusBadge';
import { formatCelsius } from '@/lib/formatUnits';
import type { DensityMode } from '@/lib/preferences/types';
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
  size?: DensityMode;
  onMarkReady?: () => void;
}

function formatEta(etaMinutes: number | null): string {
  if (etaMinutes === null) return '—';
  const hours = Math.floor(etaMinutes / 60);
  const minutes = etaMinutes % 60;
  return hours > 0 ? `${hours}h ${minutes}m` : `${minutes}m`;
}

function formatTemp(value: number | null | undefined): string {
  return value == null ? '—' : formatCelsius(value);
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
  size = 'medium',
  onMarkReady,
}: PrinterTileProps) {
  const badge = printerStatusToBadge(status, isReachable);
  const isPrinting = isReachable && status === 'Printing';
  // A finished print still sitting on the bed keeps its name until it's cleared.
  const hasJob = isReachable && (isPrinting || status === 'AwaitingClear');
  const statusChip =
    isReachable && status === 'AwaitingClear' && onMarkReady ? (
      <button
        type="button"
        className="relative z-20 shrink-0 cursor-pointer rounded-full transition-opacity hover:opacity-80 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
        aria-label={`Mark ${name} ready`}
        onClick={onMarkReady}
      >
        <StatusBadge kind={badge.kind} label={badge.label} />
      </button>
    ) : (
      <StatusBadge kind={badge.kind} label={badge.label} />
    );

  const jobLine =
    hasJob && jobName ? (
      <>
        <span className="text-foreground">{jobName}</span>
        {isPrinting && stageLabel ? ` · ${stageLabel}` : null}
      </>
    ) : status === 'AwaitingClear' && isReachable ? (
      'Ready to clear'
    ) : (
      'No active print'
    );

  const filamentDot = (
    <span
      className="size-3 shrink-0 rounded-full ring-1 ring-foreground/15"
      style={{ backgroundColor: filamentColorHex ?? 'transparent' }}
      aria-hidden="true"
    />
  );

  // Small: no thumbnail, tight single-column layout that packs many printers per page.
  if (size === 'small') {
    return (
      <Card className="gap-2 p-3">
        <div className="flex min-w-0 items-center gap-2">
          {filamentDot}
          <span className="min-w-0 flex-1 truncate text-sm font-medium">
            {name}
          </span>
          {statusChip}
        </div>
        <p className="truncate text-xs text-muted-foreground">{jobLine}</p>
        <div className="flex min-h-4 items-center gap-2">
          {isPrinting && (
            <>
              <Progress
                value={progressPercent}
                className="h-1 flex-1"
                indicatorClassName="bg-status-printing"
              />
              <span className="shrink-0 font-mono text-[11px] font-medium text-status-printing">
                {progressPercent}%
              </span>
            </>
          )}
        </div>
        <div className="flex items-center justify-between font-mono text-[11px] text-muted-foreground">
          <span>
            {formatTemp(nozzleTempC)} / {formatTemp(bedTempC)}
          </span>
          {isPrinting && <span>ETA {formatEta(etaMinutes)}</span>}
        </div>
      </Card>
    );
  }

  const isLarge = size === 'large';

  return (
    <Card className="gap-0 overflow-hidden py-0">
      <div className="relative">
        <CheckerboardThumbnail
          icon={PrinterIcon}
          label={isPrinting ? `${progressPercent}%` : undefined}
          className={cn(
            'w-full rounded-none',
            isLarge ? 'aspect-[16/10]' : 'aspect-[16/9]',
            !isReachable && 'opacity-50 grayscale',
          )}
        />
        <div className="absolute top-2 left-2 rounded-full bg-background/85 backdrop-blur-sm">
          {statusChip}
        </div>
      </div>

      <CardContent
        className={cn('flex flex-col gap-3', isLarge ? 'p-5' : 'p-[18px]')}
      >
        <div className="flex min-w-0 items-center gap-2">
          {filamentDot}
          <span
            className={cn(
              'min-w-0 flex-1 truncate font-medium',
              isLarge && 'text-lg',
            )}
          >
            {name}
          </span>
        </div>

        <p className="truncate text-sm text-muted-foreground">{jobLine}</p>

        <div className="flex min-h-4 items-center gap-3">
          {isPrinting && (
            <>
              <Progress
                value={progressPercent}
                className="h-1.5 flex-1"
                indicatorClassName="bg-status-printing"
              />
              <span className="shrink-0 font-mono text-xs font-medium text-status-printing">
                {progressPercent}%
              </span>
            </>
          )}
        </div>

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
