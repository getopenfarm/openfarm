import { LoaderIcon } from 'lucide-react';

interface PartEstimateProps {
  status?: string;
  minutes?: number | null;
  grams?: number | null;
}

export function PartEstimate({ status, minutes, grams }: PartEstimateProps) {
  if (status === 'Resolving') {
    return (
      <span className="flex items-center gap-1.5 text-sm text-muted-foreground">
        <LoaderIcon className="size-3.5 animate-spin" />
        Estimating…
      </span>
    );
  }
  return (
    <span className="font-mono text-sm">
      {minutes != null ? `${minutes} min` : 'Time unavailable'} ·{' '}
      {grams != null ? `${grams} g` : 'Weight unavailable'}
    </span>
  );
}
