import { cn } from '@/lib/utils';
import { type PrinterStatus, STATUS_META } from './status';

interface LayerStackProgressProps {
  status: PrinterStatus;
  progressPercent: number;
}

const BAND_COUNT = 18;

export function LayerStackProgress({
  status,
  progressPercent,
}: LayerStackProgressProps) {
  const clamped = Math.min(100, Math.max(0, progressPercent));
  const filledBands = Math.round((clamped / 100) * BAND_COUNT);
  const meta = STATUS_META[status];

  return (
    <div
      className="flex h-16 w-full flex-col-reverse gap-[2px]"
      role="img"
      aria-label={`${clamped}% printed`}
    >
      {Array.from({ length: BAND_COUNT }).map((_, index) => {
        const isFilled = index < filledBands;
        const isLeadingEdge = isFilled && index === filledBands - 1;
        return (
          <div
            key={
              // biome-ignore lint/suspicious/noArrayIndexKey: fixed-length static band stack
              index
            }
            className={cn(
              'w-full flex-1 rounded-[1px]',
              isFilled ? cn(meta.dotClass, meta.textClass) : 'bg-muted',
              isLeadingEdge && 'shadow-[0_0_6px_currentColor]',
            )}
          />
        );
      })}
    </div>
  );
}
