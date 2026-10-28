import type { ReactNode } from 'react';
import { cn } from '@/lib/utils';

export interface KpiStripCell {
  label: string;
  value: ReactNode;
  sub?: ReactNode;
  dotClassName?: string;
}

/** The compact form of a row of KpiCards: one bordered strip, for a detail page whose own content leads. */
export function KpiStrip({ cells }: { cells: KpiStripCell[] }) {
  return (
    <dl className="grid grid-cols-2 gap-px overflow-hidden rounded-lg border bg-border lg:grid-cols-4">
      {cells.map((cell) => (
        <div key={cell.label} className="bg-card px-4 py-2.5">
          <dt className="flex items-center gap-1.5 text-xs text-muted-foreground">
            <span
              className={cn(
                'size-1.5 shrink-0 rounded-full',
                cell.dotClassName ?? 'bg-muted-foreground',
              )}
            />
            {cell.label}
          </dt>
          <dd className="mt-0.5 text-base font-semibold tabular-nums">
            {cell.value}
          </dd>
          {cell.sub && (
            <dd className="text-xs text-muted-foreground">{cell.sub}</dd>
          )}
        </div>
      ))}
    </dl>
  );
}
