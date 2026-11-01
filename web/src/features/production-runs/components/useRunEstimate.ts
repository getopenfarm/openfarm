import { useQueries } from '@tanstack/react-query';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import { sumPrintEstimates } from '@/lib/printEstimates';
import { demandKey, type PlanLine, previewPlan } from './productionPlanner';

export type RunTargetKind = 'Part' | 'Assembly';
export type EstimateLine = PlanLine & { lineId: string };

/** One observer per line; identity is kept even when another line is invalid or unresolved. */
export function useRunEstimate(lines: EstimateLine[]) {
  const inputs = lines.map(({ lineId: _lineId, ...line }) => line);
  const keys = inputs.map((line) => JSON.stringify(line));
  const unique = [
    ...new Map(inputs.map((line, index) => [keys[index], line])).entries(),
  ];
  const queries = useQueries({
    queries: unique.map(([, line]) => ({
      queryKey: [demandKey, 'preview', line],
      queryFn: async ({ signal }: { signal: AbortSignal }) => {
        try {
          return await previewPlan(line, undefined, signal);
        } catch (error) {
          throw new Error(
            await extractErrorDetail(
              error,
              error instanceof Error
                ? error.message
                : 'Check quantities and print requirements, then retry.',
            ),
          );
        }
      },
      enabled: Number.isInteger(line.quantity) && (line.quantity ?? 0) > 0,
      staleTime: 0,
    })),
  });
  const byLine = new Map(
    lines.map((line, index) => {
      const query = queries[unique.findIndex(([key]) => key === keys[index])];
      const valid = Number.isInteger(line.quantity) && (line.quantity ?? 0) > 0;
      return [
        line.lineId,
        {
          valid,
          plan:
            valid && query.isSuccess && !query.isFetching
              ? query.data
              : undefined,
          isPending: valid && (query.isPending || query.isFetching),
          isError: valid && query.isError,
          error: query.error,
          refetch: query.refetch,
        },
      ] as const;
    }),
  );
  const states = [...byLine.values()];
  const plans = states.flatMap((state) => (state.plan ? [state.plan] : []));
  const components = plans.flatMap((plan) => plan.parts);
  const total = sumPrintEstimates(plans.map((plan) => plan.estimate));
  return {
    byLine,
    plates: plans.reduce((sum, plan) => sum + plan.plates, 0),
    units: components.reduce((sum, part) => sum + part.plannedUnits, 0),
    time: total.time,
    filament: total.filament,
    approximate: total.approximate,
    partIds: [...new Set(components.map((part) => part.partId))],
    isReady: states.length > 0 && states.every((state) => !!state.plan),
    isInvalid: states.some((state) => !state.valid),
    isPending: states.some((state) => state.isPending),
    isError: states.some((state) => state.isError),
    refetch: () =>
      Promise.all(
        queries
          .filter(
            (_, index) =>
              Number.isInteger(unique[index][1].quantity) &&
              (unique[index][1].quantity ?? 0) > 0,
          )
          .map((query) => query.refetch()),
      ),
  };
}

export type RunEstimate = ReturnType<typeof useRunEstimate>;
export type RunLineEstimate =
  RunEstimate['byLine'] extends Map<string, infer T> ? T : never;
