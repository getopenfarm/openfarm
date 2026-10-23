import type { SpoolResponse } from '@/lib/api/generated/endpoints.schemas';

/** Full rolls of one color and spool weight: the same roll, many times over. */
export interface ShelfGroup {
  key: string;
  filamentVariantId: string;
  colorName: string;
  colorHex: string;
  spoolWeightGrams: number;
  spools: SpoolResponse[];
}

/** Shelf spools as groups of identical full rolls (color × spool weight), largest first, and the partial rolls, which differ, apart. */
export function groupShelf(spools: SpoolResponse[]) {
  const groups = new Map<string, ShelfGroup>();
  const partial: SpoolResponse[] = [];
  for (const spool of spools) {
    const weight = spool.spoolWeightGrams ?? 0;
    if ((spool.remainingWeightGrams ?? 0) < weight) {
      partial.push(spool);
      continue;
    }
    const key = `${spool.filamentVariantId}:${weight}`;
    const group = groups.get(key) ?? {
      key,
      filamentVariantId: spool.filamentVariantId ?? '',
      colorName: spool.colorName ?? '',
      colorHex: spool.colorHex ?? '',
      spoolWeightGrams: weight,
      spools: [],
    };
    group.spools.push(spool);
    groups.set(key, group);
  }
  return {
    full: [...groups.values()].sort(
      (a, b) => b.spools.length - a.spools.length,
    ),
    partial,
  };
}
