import { useQueries } from '@tanstack/react-query';
import type { PartResponse } from '@/lib/api/generated/endpoints.schemas';
import { getOpenFarmApiEndpointsPartsGetPartEndpointQueryOptions } from '@/lib/api/generated/parts/parts';
import { demandKey, type PlanLine, previewPlan } from './productionPlanner';

export type RunTargetKind = 'Part' | 'Assembly';
export type EstimateLine = PlanLine;

/** Display the same server plans that queue submission validates; no duplicated BOM/plate arithmetic. */
export function useRunEstimate(lines: EstimateLine[]) {
  const finite = lines.filter((l) => l.quantity != null && l.quantity > 0);
  const queries = useQueries({
    queries: finite.map((line) => ({
      queryKey: [demandKey, 'preview', line],
      queryFn: ({ signal }: { signal: AbortSignal }) =>
        previewPlan(line, undefined, signal),
      staleTime: 0,
    })),
  });
  const plans = queries.flatMap((q) => (q.data ? [q.data] : []));
  const ids = [
    ...new Set([
      ...lines.filter((l) => l.targetKind === 'Part').map((l) => l.targetId),
      ...plans.flatMap((p) => p.parts.map((p) => p.partId)),
    ]),
  ];
  const partQueries = useQueries({
    queries: ids.map((id) =>
      getOpenFarmApiEndpointsPartsGetPartEndpointQueryOptions(id),
    ),
  });
  const parts = new Map<string, PartResponse>();
  ids.forEach((id, i) => {
    const part = partQueries[i]?.data?.data;
    if (part) parts.set(id, part);
  });
  return {
    plates: plans.reduce((sum, p) => sum + p.plates, 0),
    units: plans.reduce(
      (sum, p) => sum + p.parts.reduce((n, part) => n + part.plannedUnits, 0),
      0,
    ),
    minutes: plans.reduce((sum, p) => sum + (p.minutes ?? 0), 0),
    grams: plans.reduce((sum, p) => sum + (p.grams ?? 0), 0),
    unestimated: plans.flatMap((p) => p.parts).filter((p) => p.minutes == null)
      .length,
    isPending: queries.some((q) => q.isPending || q.isFetching),
    isError: queries.some((q) => q.isError),
    refetch: () => Promise.all(queries.map((query) => query.refetch())),
    plans,
    parts,
  };
}
