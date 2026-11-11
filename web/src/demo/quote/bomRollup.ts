import type { BomLineTargetKind } from '@/features/assemblies/components/BomLineEditor';
import type {
  ExplodedNonPrintedComponentResponse,
  ExplodedPartResponse,
} from '@/lib/api/generated/endpoints.schemas';

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

const colorKey = (
  material?: string | null,
  color?: string | null,
  variantId?: string | null,
) =>
  variantId
    ? `variant:${variantId}`
    : `${material ?? ''}|${(color ?? '').toUpperCase()}`;

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

interface FilamentPrice {
  pricePerKg?: number | null;
  variants?: { id?: string; pricePerKg?: number | null }[] | null;
}

/** Resolve prices and group by selected variant, falling back to file color when unselected. */
export function filamentCost(
  rows: {
    materialType?: string | null;
    colorHex?: string | null;
    variantId?: string | null;
    grams?: number | null;
    missing?: number;
  }[],
  filaments: FilamentPrice[],
) {
  const prices = new Map<string, number | null | undefined>();
  for (const filament of filaments)
    for (const variant of filament.variants ?? [])
      if (variant.id)
        prices.set(variant.id, variant.pricePerKg ?? filament.pricePerKg);
  let total = 0;
  let priced = 0;
  let missingSelection = 0;
  let missingPrice = 0;
  let missingGrams = 0;
  const colors = new Map<
    string,
    { total: number; priced: number; missing: number }
  >();
  for (const row of rows) {
    const key = colorKey(row.materialType, row.colorHex, row.variantId);
    const color = colors.get(key) ?? { total: 0, priced: 0, missing: 0 };
    const variantId = row.variantId;
    const price = variantId ? prices.get(variantId) : null;
    if (row.grams == null || (row.missing ?? 0) > 0) missingGrams++;
    if (!variantId) missingSelection++;
    else if (price == null) missingPrice++;
    if (row.grams == null || price == null || (row.missing ?? 0) > 0)
      color.missing++;
    if (row.grams != null && price != null) {
      const cost = (row.grams / 1000) * price;
      total += cost;
      color.total += cost;
      color.priced++;
      priced++;
    }
    colors.set(key, color);
  }
  return {
    total,
    priced,
    missingSelection,
    missingPrice,
    missingGrams,
    colors,
    incomplete: missingSelection + missingPrice + missingGrams > 0,
  };
}

export const filamentColorKey = colorKey;
