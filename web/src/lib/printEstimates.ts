import type {
  EstimateMetric,
  PrintEstimate,
} from './api/generated/endpoints.schemas';
import { formatMinutes } from './formatMinutes';
import { formatWeight } from './formatUnits';

export const emptyPrintEstimate = (): PrintEstimate => ({
  time: { value: 0, missing: 0 },
  filament: { value: 0, missing: 0 },
  approximate: false,
  colors: [],
});
export const unknownPrintEstimate = (): PrintEstimate => ({
  time: { value: null, missing: 1 },
  filament: { value: null, missing: 1 },
  approximate: false,
  colors: [],
});

/** Aggregate server-calculated contributions. No quantity scaling or material fallback lives in the UI. */
export function sumPrintEstimates(
  estimates: (PrintEstimate | null | undefined)[],
): PrintEstimate {
  const rows = estimates.map((estimate) => estimate ?? unknownPrintEstimate());
  const metric = (key: 'time' | 'filament'): EstimateMetric => {
    const known = rows.filter((row) => row[key]?.value != null);
    return {
      value:
        rows.length === 0
          ? 0
          : known.length
            ? known.reduce((sum, row) => sum + (row[key]?.value ?? 0), 0)
            : null,
      missing: rows.reduce((sum, row) => sum + (row[key]?.missing ?? 1), 0),
    };
  };
  return {
    time: metric('time'),
    filament: metric('filament'),
    approximate: rows.some((row) => row.approximate),
    colors: rows.flatMap((row) => row.colors ?? []),
  };
}

export function formatEstimateMetric(
  metric: EstimateMetric | null | undefined,
  kind: 'time' | 'filament',
  approximate = true,
) {
  if (metric?.value == null) return 'Unavailable';
  return `${(metric.missing ?? 0) > 0 ? 'Partial · ' : ''}${approximate ? '≈ ' : ''}${kind === 'time' ? formatMinutes(metric.value) : formatWeight(metric.value)}`;
}
