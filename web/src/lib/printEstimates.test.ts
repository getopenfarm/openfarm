import { expect, it } from 'vitest';
import { formatEstimateMetric, sumPrintEstimates } from './printEstimates';

it('keeps missing time independent from known zero filament', () => {
  const estimate = sumPrintEstimates([
    { time: { value: null, missing: 1 }, filament: { value: 0, missing: 0 } },
  ]);
  expect(formatEstimateMetric(estimate.time, 'time')).toBe('Unavailable');
  expect(formatEstimateMetric(estimate.filament, 'filament')).toBe('≈ 0 g');
});
it('marks known subtotals partial and preserves approximate provenance', () => {
  const estimate = sumPrintEstimates([
    {
      time: { value: 30, missing: 0 },
      filament: { value: null, missing: 1 },
      approximate: true,
    },
    { time: { value: null, missing: 1 }, filament: { value: 4, missing: 0 } },
  ]);
  expect(formatEstimateMetric(estimate.time, 'time')).toBe('Partial · ≈ 30m');
  expect(formatEstimateMetric(estimate.filament, 'filament')).toBe(
    'Partial · ≈ 4 g',
  );
  expect(estimate.approximate).toBe(true);
});
it('distinguishes no remaining work from missing server estimates', () => {
  expect(formatEstimateMetric(sumPrintEstimates([]).time, 'time')).toBe('≈ 0m');
  expect(
    formatEstimateMetric(sumPrintEstimates([undefined]).time, 'time'),
  ).toBe('Unavailable');
});
