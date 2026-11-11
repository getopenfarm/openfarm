import type {
  AssemblyQuote,
  PartSummaryResponse,
  PrinterModelCostResponse,
  QuoteAssemblyRequest,
  QuoteColor,
  QuoteLineRequest,
} from '@/lib/api/generated/endpoints.schemas';
import type { Assembly, Filament, Part } from '../farm';
import { filamentColorKey, filamentCost, rollUp } from './bomRollup';
import { machineCosts } from './machineCosts';
import { profilePricing } from './pricing';

export interface QuoteFarm {
  parts: Part[];
  assemblies: Assembly[];
  filaments: Filament[];
  summary: (part: Part) => PartSummaryResponse;
  models: PrinterModelCostResponse[];
  failurePercent: number;
  profiles: { id: string; name: string }[];
}

/**
 * The demo's POST /api/v1/assemblies/quote: AssemblyQuoteService's rules on the in-memory farm. Demo assemblies hold only
 * parts and the demo has no cost items, so cost-item lines count as missing.
 */
export function demoQuote(
  body: QuoteAssemblyRequest,
  farm: QuoteFarm,
): AssemblyQuote {
  const saved = farm.assemblies.find((a) => a.id === body.assemblyId);
  const lines: QuoteLineRequest[] =
    body.lines ??
    saved?.lines.map((l) => ({
      targetKind: 'Part',
      targetId: l.partId,
      quantity: l.quantity,
    })) ??
    [];
  const draft = lines.map((l) => ({
    targetKind: (l.targetKind ?? 'Part') as 'Part',
    targetId: l.targetId ?? '',
    quantity: l.quantity ?? 0,
  }));
  const subs = new Map(
    draft
      .filter((l) => l.targetKind === ('Assembly' as string))
      .map((l) => [
        l.targetId,
        {
          printedParts:
            farm.assemblies
              .find((a) => a.id === l.targetId)
              ?.lines.map((p) => ({
                partId: p.partId,
                quantity: p.quantity,
              })) ?? [],
          nonPrintedComponents: [],
        },
      ]),
  );
  const total = rollUp(draft, subs);

  // Print estimate: every demo part prints one colour.
  const colors = new Map<string, QuoteColor>();
  let minutes = 0;
  for (const [partId, quantity] of total.parts) {
    const part = farm.parts.find((p) => p.id === partId);
    if (!part) continue;
    const plates = quantity / part.unitsPerPlate;
    minutes += part.minutesPerPlate * plates;
    const filament = farm.filaments.find((f) =>
      f.variants.some((v) => v.id === part.variantId),
    );
    const v = filament?.variants.find((x) => x.id === part.variantId);
    const color = colors.get(part.variantId) ?? {
      materialType: filament?.materialType,
      colorHex: v?.colorHex,
      variantId: part.variantId,
      grams: 0,
      missing: 0,
      name: `${filament?.vendorName} ${filament?.material} · ${v?.colorName}`,
      swatchHex: v?.colorHex,
      cost: 0,
      costPriced: 0,
      costMissing: 0,
    };
    color.grams = (color.grams ?? 0) + part.gramsPerPlate * plates;
    colors.set(part.variantId, color);
  }
  const rows = [...colors.values()];
  const filament = filamentCost(rows, farm.filaments);
  for (const row of rows) {
    const c = filament.colors.get(
      filamentColorKey(row.materialType, row.colorHex, row.variantId),
    );
    row.cost = c?.total ?? 0;
    row.costPriced = c?.priced ?? 0;
    row.costMissing = c?.missing ?? 0;
  }
  const grams = rows.reduce((sum, r) => sum + (r.grams ?? 0), 0);

  const machine = machineCosts(
    total.parts,
    farm.parts.map(farm.summary),
    farm.models,
  );
  const missingAdditional = body.additionalCosts?.length ?? 0;
  const failure =
    (farm.failurePercent / 100) * (filament.total + machine.total);
  const printPartial = filament.incomplete || machine.incomplete;
  const available =
    machine.depreciation.priced > 0 ||
    machine.electricity.priced > 0 ||
    filament.priced > 0 ||
    total.parts.size === 0;
  const amount = filament.total + machine.total + failure;
  // Demo assemblies save no pricing rule.
  const pricing = body.pricing ?? null;
  const cost = available ? amount : 0;
  const priceable = available || pricing?.mode === 'Price';
  return {
    parts: total.parts.size,
    components: total.components.size,
    print: {
      time: { value: minutes, missing: 0 },
      filament: { value: grams, missing: 0 },
      approximate: false,
      colors: rows,
    },
    colors: rows,
    filament: {
      total: filament.total,
      priced: filament.priced,
      missingSelection: filament.missingSelection,
      missingPrice: filament.missingPrice,
      missingGrams: filament.missingGrams,
      incomplete: filament.incomplete,
    },
    depreciation: machine.depreciation,
    electricity: machine.electricity,
    failurePercent: farm.failurePercent,
    failure,
    failureAvailable: available,
    printPartial,
    breakdown: [],
    costsPartial: missingAdditional > 0,
    shipping: 0,
    total: {
      amount,
      partial: printPartial || missingAdditional > 0,
      available,
    },
    notes:
      missingAdditional > 0 ? ['Cost items have missing prices or usage.'] : [],
    profiles: farm.profiles.map((p) => ({
      profileId: p.id,
      name: p.name,
      fees: [],
      feePercent: 0,
      fixedFee: 0,
      pricing: profilePricing(
        cost,
        { name: p.name, feePercent: 0, fixedFee: 0 },
        priceable && pricing
          ? {
              mode: (pricing.mode ?? 'Price') as 'Price',
              value: pricing.value ?? 0,
            }
          : null,
      ),
    })),
  };
}
