import { LoaderIcon } from 'lucide-react';
import type { PrintEstimate } from '@/lib/api/generated/endpoints.schemas';
import { formatMinutes } from '@/lib/formatMinutes';
import { formatGrams } from '@/lib/formatUnits';
import { formatEstimateMetric } from '@/lib/printEstimates';

/**
 * The part's filament total with start-of-print purge: the sum of its colours' grams when every colour has them,
 * else the slicer's total (parts not yet reprocessed carry no per-colour grams).
 */
export function filamentGrams(
  requirements: { estimatedGrams?: number | null }[],
  fallback?: number | null,
) {
  const known = requirements.every((r) => r.estimatedGrams != null);
  return requirements.length > 0 && known
    ? requirements.reduce((sum, r) => sum + (r.estimatedGrams ?? 0), 0)
    : fallback;
}

interface PartEstimateProps {
  estimate?: PrintEstimate | null;
  status?: string;
  minutes?: number | null;
  grams?: number | null;
  /** `lg` renders time and weight as two headline stats. */
  size?: 'inline' | 'lg';
  /** `grams` is the purge-inclusive per-colour sum, not the slicer's number. */
  includesPurge?: boolean;
}

export function PartEstimate({
  status,
  estimate,
  minutes,
  grams,
  size = 'inline',
  includesPurge = false,
}: PartEstimateProps) {
  if (status === 'Resolving') {
    return (
      <span className="flex items-center gap-1.5 text-sm text-muted-foreground">
        <LoaderIcon className="size-3.5 animate-spin" />
        Estimating…
      </span>
    );
  }
  const timeText = estimate
    ? formatEstimateMetric(estimate.time, 'time', !!estimate.approximate)
    : minutes != null
      ? formatMinutes(minutes)
      : '—';
  const gramsText = estimate
    ? formatEstimateMetric(
        estimate.filament,
        'filament',
        !!estimate.approximate,
      )
    : grams != null
      ? formatGrams(grams)
      : '—';
  if (size === 'lg') {
    return (
      <dl className="grid grid-cols-2 gap-4">
        <div>
          <dt className="text-xs text-muted-foreground">Print time</dt>
          <dd className="font-heading text-2xl font-semibold tabular-nums">
            {timeText}
          </dd>
        </div>
        <div>
          <dt className="text-xs text-muted-foreground">
            Filament{includesPurge && ' · incl. purge'}
          </dt>
          <dd className="font-heading text-2xl font-semibold tabular-nums">
            {gramsText}
          </dd>
        </div>
      </dl>
    );
  }
  return (
    <span className="font-mono text-sm">
      {estimate
        ? timeText
        : minutes != null
          ? formatMinutes(minutes)
          : 'Time unavailable'}{' '}
      ·{' '}
      {estimate
        ? gramsText
        : grams != null
          ? formatGrams(grams)
          : 'Weight unavailable'}
    </span>
  );
}
