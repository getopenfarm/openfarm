import type { CSSProperties } from 'react';
import { cn } from '@/lib/utils';

export interface Plates {
  completed?: number;
  printing?: number;
  queued?: number;
  needsAction?: number;
  writtenOff?: number;
}

/** Past this many plates a cell per plate gets too thin to read, so the strip draws proportional segments. */
const MAX_CELLS = 60;

const HATCH: CSSProperties = {
  backgroundImage:
    'repeating-linear-gradient(135deg, var(--chart-cancelled) 0 2px, transparent 2px 4px)',
};

const STATES = [
  { key: 'completed', className: 'bg-(--chart-completed)', label: 'done' },
  { key: 'writtenOff', className: '', style: HATCH, label: 'written off' },
  { key: 'printing', className: 'bg-(--chart-printing)', label: 'printing' },
  { key: 'needsAction', className: 'bg-(--chart-failed)', label: 'failed' },
  { key: 'queued', className: 'bg-muted-foreground/25', label: 'waiting' },
] as const;

export function describePlates(plates: Plates) {
  const total = STATES.reduce((sum, { key }) => sum + (plates[key] ?? 0), 0);
  const rest = STATES.filter(
    ({ key }) => key !== 'completed' && (plates[key] ?? 0) > 0,
  ).map(({ key, label }) => `${plates[key]} ${label}`);
  return [`${plates.completed ?? 0} of ${total} plates done`, ...rest].join(
    ', ',
  );
}

/**
 * A run's plates, one cell each, in the order work moves: done, written off, printing, failed, waiting. The atom of
 * work is the plate, so a failed or waiting plate is never hidden inside a percentage.
 */
export function PlateStrip({
  plates,
  size = 'md',
  className,
}: {
  plates: Plates;
  size?: 'sm' | 'md';
  className?: string;
}) {
  const total = STATES.reduce((sum, { key }) => sum + (plates[key] ?? 0), 0);
  if (total === 0) return null;
  const height = size === 'sm' ? 'h-1.5' : 'h-2.5';
  const label = describePlates(plates);

  if (total > MAX_CELLS) {
    return (
      <div
        role="img"
        aria-label={label}
        className={cn(
          'flex w-full overflow-hidden rounded-sm bg-muted-foreground/25',
          height,
          className,
        )}
      >
        {STATES.map((state) => {
          const count = plates[state.key] ?? 0;
          return count > 0 ? (
            <div
              key={state.key}
              className={state.className}
              style={{
                ...('style' in state ? state.style : undefined),
                width: `${(count / total) * 100}%`,
              }}
            />
          ) : null;
        })}
      </div>
    );
  }

  return (
    <div
      role="img"
      aria-label={label}
      className={cn('flex w-full gap-0.5', height, className)}
    >
      {STATES.flatMap((state) =>
        Array.from({ length: plates[state.key] ?? 0 }, (_, index) => (
          <div
            // biome-ignore lint/suspicious/noArrayIndexKey: identical cells; only their count and state matter.
            key={`${state.key}-${index}`}
            className={cn('min-w-0.5 flex-1 rounded-[2px]', state.className)}
            style={'style' in state ? state.style : undefined}
          />
        )),
      )}
    </div>
  );
}
