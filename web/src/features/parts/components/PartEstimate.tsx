import { LoaderIcon } from 'lucide-react';
import { formatMinutes } from '@/lib/formatMinutes';
import { formatGrams } from '@/lib/formatUnits';

interface PartEstimateProps {
  status?: string;
  minutes?: number | null;
  grams?: number | null;
  /** `lg` renders time and weight as two headline stats. */
  size?: 'inline' | 'lg';
}

export function PartEstimate({
  status,
  minutes,
  grams,
  size = 'inline',
}: PartEstimateProps) {
  if (status === 'Resolving') {
    return (
      <span className="flex items-center gap-1.5 text-sm text-muted-foreground">
        <LoaderIcon className="size-3.5 animate-spin" />
        Estimating…
      </span>
    );
  }
  if (size === 'lg') {
    return (
      <dl className="grid grid-cols-2 gap-4">
        <div>
          <dt className="text-xs text-muted-foreground">Print time</dt>
          <dd className="font-heading text-2xl font-semibold tabular-nums">
            {minutes != null ? formatMinutes(minutes) : '—'}
          </dd>
        </div>
        <div>
          <dt className="text-xs text-muted-foreground">Filament</dt>
          <dd className="font-heading text-2xl font-semibold tabular-nums">
            {grams != null ? formatGrams(grams) : '—'}
          </dd>
        </div>
      </dl>
    );
  }
  return (
    <span className="font-mono text-sm">
      {minutes != null ? formatMinutes(minutes) : 'Time unavailable'} ·{' '}
      {grams != null ? formatGrams(grams) : 'Weight unavailable'}
    </span>
  );
}
