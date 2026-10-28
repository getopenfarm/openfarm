import { useQueries } from '@tanstack/react-query';
import { rollUp } from '@/features/assemblies/components/bomRollup';
import { getOpenFarmApiEndpointsAssembliesExplodeAssemblyEndpointQueryOptions } from '@/lib/api/generated/assemblies/assemblies';
import type { PartResponse } from '@/lib/api/generated/endpoints.schemas';
import { getOpenFarmApiEndpointsPartsGetPartEndpointQueryOptions } from '@/lib/api/generated/parts/parts';

export type RunTargetKind = 'Part' | 'Assembly';

export interface EstimateLine {
  targetKind: RunTargetKind;
  targetId: string;
  /** null = unlimited: no plates up front, so nothing to estimate. */
  quantity: number | null;
}

/**
 * What finite lines will queue, as the server makes it: each line's parts round up to whole plates (lines are not
 * pooled), from the parts' own per-plate estimates. Only the parts the lines use are fetched, one cached query each.
 * `unestimated` counts parts with no time estimate.
 */
export function useRunEstimate(lines: EstimateLine[]) {
  const assemblyIds = [
    ...new Set(
      lines.filter((l) => l.targetKind === 'Assembly').map((l) => l.targetId),
    ),
  ];
  const explosions = useQueries({
    queries: assemblyIds.map((id) =>
      getOpenFarmApiEndpointsAssembliesExplodeAssemblyEndpointQueryOptions(id, {
        quantity: 1,
      }),
    ),
  });
  const byAssembly = new Map(
    assemblyIds.map((id, i) => [id, explosions[i]?.data?.data || undefined]),
  );
  const partIds = [
    ...new Set([
      ...lines.filter((l) => l.targetKind === 'Part').map((l) => l.targetId),
      ...[...byAssembly.values()].flatMap(
        (e) => e?.printedParts?.map((p) => p.partId ?? '') ?? [],
      ),
    ]),
  ].filter(Boolean);
  const partQueries = useQueries({
    queries: partIds.map((id) =>
      getOpenFarmApiEndpointsPartsGetPartEndpointQueryOptions(id),
    ),
  });
  const parts = new Map<string, PartResponse>();
  partIds.forEach((id, i) => {
    const part = partQueries[i]?.data?.data as PartResponse | undefined;
    if (part?.id) parts.set(id, part);
  });

  let plates = 0;
  let units = 0;
  let minutes = 0;
  let grams = 0;
  let unestimated = 0;
  for (const line of lines) {
    if (line.quantity == null || !(line.quantity > 0)) continue;
    const { parts: needs } = rollUp(
      [{ ...line, quantity: line.quantity }],
      byAssembly,
    );
    for (const [partId, need] of needs) {
      const part = parts.get(partId);
      const perPlate = Math.max(1, part?.unitsPerPrint ?? 1);
      const count = Math.ceil(need / perPlate);
      plates += count;
      units += count * perPlate;
      if (part?.estimatedPrintTimeMinutes == null) unestimated++;
      minutes += count * (part?.estimatedPrintTimeMinutes ?? 0);
      grams += count * (part?.estimatedFilamentWeightGrams ?? 0);
    }
  }

  return {
    plates,
    units,
    minutes,
    grams,
    unestimated,
    isPending:
      explosions.some((q) => q.isPending) ||
      partQueries.some((q) => q.isPending),
    parts,
  };
}
