import type {
  ExplodedNonPrintedComponentResponse,
  ExplodedPartResponse,
} from '@/lib/api/generated/endpoints.schemas';
import type { BomLineTargetKind } from './BomLineEditor';

interface RollupLine {
  targetKind: BomLineTargetKind;
  targetId: string;
  quantity: number;
}

interface SubExplosion {
  printedParts?: ExplodedPartResponse[];
  nonPrintedComponents?: ExplodedNonPrintedComponentResponse[];
}

const add = (map: Map<string, number>, id: string, qty: number) =>
  map.set(id, (map.get(id) ?? 0) + qty);

/** Draft lines + each sub-assembly's quantity-1 explosion, scaled by line qty. Unloaded explosions contribute nothing yet. */
export function rollUp(
  lines: RollupLine[],
  subExplosions: Map<string, SubExplosion | undefined>,
) {
  const parts = new Map<string, number>();
  const components = new Map<string, number>();
  for (const line of lines) {
    // Mid-typing draft values (blank, 0, negative) contribute nothing; the server never has them.
    if (!(line.quantity > 0)) continue;
    const qty = line.quantity;
    if (line.targetKind === 'Part') add(parts, line.targetId, qty);
    else if (line.targetKind === 'NonPrintedComponent')
      add(components, line.targetId, qty);
    else {
      const sub = subExplosions.get(line.targetId);
      for (const p of sub?.printedParts ?? [])
        if (p.partId) add(parts, p.partId, (p.quantity ?? 0) * qty);
      for (const c of sub?.nonPrintedComponents ?? [])
        if (c.nonPrintedComponentId)
          add(components, c.nonPrintedComponentId, (c.quantity ?? 0) * qty);
    }
  }
  return { parts, components };
}

interface PartEstimate {
  id?: string;
  unitsPerPrint?: number;
  estimatedPrintTimeMinutes?: number | null;
  estimatedFilamentWeightGrams?: number | null;
}

/** Estimates are per plate of unitsPerPrint units, so each unit costs estimate / unitsPerPrint. No rounding up to whole plates. */
export function printTotals(
  parts: Map<string, number>,
  catalog: PartEstimate[],
) {
  let minutes = 0;
  let grams = 0;
  let unestimated = 0;
  for (const [id, need] of parts) {
    const est = catalog.find((p) => p.id === id);
    const plates = need / Math.max(1, est?.unitsPerPrint ?? 1);
    if (est?.estimatedPrintTimeMinutes == null) unestimated++;
    minutes += plates * (est?.estimatedPrintTimeMinutes ?? 0);
    grams += plates * (est?.estimatedFilamentWeightGrams ?? 0);
  }
  return { minutes, grams, unestimated };
}

/** Σ qty x unitCost; components without a cost are counted, not guessed. */
export function hardwareCost(
  components: Map<string, number>,
  catalog: { id?: string; unitCost?: number | null }[],
) {
  let total = 0;
  let missingCost = 0;
  for (const [id, qty] of components) {
    const cost = catalog.find((c) => c.id === id)?.unitCost;
    if (cost == null) missingCost++;
    else total += qty * cost;
  }
  return { total, missingCost };
}
