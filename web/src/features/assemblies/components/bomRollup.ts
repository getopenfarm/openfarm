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
  /** Per-requirement grams per plate set, purge included. */
  filamentByColor?: {
    materialType?: string | null;
    colorHex?: string | null;
    grams?: number | null;
    narrowFilamentVariantId?: string | null;
  }[];
}

export interface ColorTotal {
  variantId?: string;
  materialType: string | null;
  /** null = grams we cannot attribute to a colour (part not reprocessed, or several colours without a split). */
  colorHex: string | null;
  grams: number;
}

const colorKey = (
  material?: string | null,
  color?: string | null,
  variantId?: string | null,
) =>
  variantId
    ? `variant:${variantId}`
    : `${material ?? ''}|${(color ?? '').toUpperCase()}`;

/**
 * Estimates are per plate of unitsPerPrint units, so each unit costs estimate / unitsPerPrint. No rounding up to whole plates.
 * Selected variants stay distinct; unselected requirements group by file color.
 * `grams` is the sum of `colors`, so the rows always add up to the total.
 */
export function printTotals(
  parts: Map<string, number>,
  catalog: PartEstimate[],
) {
  let minutes = 0;
  let unestimated = 0;
  const byColor = new Map<string, ColorTotal>();
  const addGrams = (
    material: string | null | undefined,
    color: string | null | undefined,
    grams: number,
    variantId?: string | null,
  ) => {
    const key = colorKey(material, color, variantId);
    const total = byColor.get(key) ?? {
      materialType: material ?? null,
      colorHex: color ?? null,
      grams: 0,
      ...(variantId ? { variantId } : {}),
    };
    total.grams += grams;
    byColor.set(key, total);
  };

  for (const [id, need] of parts) {
    const est = catalog.find((p) => p.id === id);
    const plates = need / Math.max(1, est?.unitsPerPrint ?? 1);
    if (est?.estimatedPrintTimeMinutes == null) unestimated++;
    minutes += plates * (est?.estimatedPrintTimeMinutes ?? 0);

    const colors = est?.filamentByColor ?? [];
    if (colors.length > 0 && colors.every((c) => c.grams != null)) {
      for (const c of colors)
        addGrams(
          c.materialType,
          c.colorHex,
          plates * (c.grams ?? 0),
          c.narrowFilamentVariantId,
        );
    } else if (est?.estimatedFilamentWeightGrams != null) {
      // One colour: the part total is that colour's grams (just without purge). Several, unsplit: unknown.
      const sole = colors.length === 1 ? colors[0] : undefined;
      addGrams(
        sole?.materialType,
        sole?.colorHex,
        plates * est.estimatedFilamentWeightGrams,
        sole?.narrowFilamentVariantId,
      );
    }
  }

  const colors = [...byColor.values()].sort(
    (a, b) =>
      Number(a.colorHex == null) - Number(b.colorHex == null) ||
      b.grams - a.grams,
  );
  return {
    minutes,
    grams: colors.reduce((sum, c) => sum + c.grams, 0),
    unestimated,
    colors,
  };
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

interface FilamentPrice {
  pricePerKg?: number | null;
  variants?: { id?: string; pricePerKg?: number | null }[] | null;
}

/** Resolve prices and group by selected variant, falling back to file color when unselected. */
export function filamentCost(
  parts: Map<string, number>,
  catalog: PartEstimate[],
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
  for (const [id, quantity] of parts) {
    const part = catalog.find((p) => p.id === id);
    const scale = quantity / Math.max(1, part?.unitsPerPrint ?? 1);
    const requirements = part?.filamentByColor ?? [];
    const splitKnown =
      requirements.length > 0 && requirements.every((r) => r.grams != null);
    const rows = splitKnown
      ? requirements
      : requirements.length === 1
        ? [{ ...requirements[0], grams: part?.estimatedFilamentWeightGrams }]
        : requirements.length > 1
          ? requirements.map((requirement) => ({ ...requirement, grams: null }))
          : [{ grams: part?.estimatedFilamentWeightGrams }];
    for (const row of rows) {
      const key = colorKey(
        row.materialType,
        row.colorHex,
        row.narrowFilamentVariantId,
      );
      const color = colors.get(key) ?? { total: 0, priced: 0, missing: 0 };
      const variantId = row.narrowFilamentVariantId;
      const price = variantId ? prices.get(variantId) : null;
      if (row.grams == null) missingGrams++;
      if (!variantId) missingSelection++;
      else if (price == null) missingPrice++;
      if (row.grams == null || price == null) color.missing++;
      else {
        const cost = ((row.grams * scale) / 1000) * price;
        total += cost;
        color.total += cost;
        color.priced++;
        priced++;
      }
      colors.set(key, color);
    }
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
