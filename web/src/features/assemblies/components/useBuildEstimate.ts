import { useQueries } from '@tanstack/react-query';
import { usage } from '@/features/cost-catalog/costing';
import { useGeneralSettings } from '@/features/settings/generalSettings';
import { usePrinterCosts } from '@/features/settings/printerCosts';
import { getOpenFarmApiEndpointsAssembliesExplodeAssemblyEndpointQueryOptions } from '@/lib/api/generated/assemblies/assemblies';
import type { AssemblyCostInput } from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsFilamentsListFilamentsEndpoint } from '@/lib/api/generated/filaments/filaments';
import type { EditableLine } from './BomLineEditor';
import { filamentCost, hardwareCost, printTotals, rollUp } from './bomRollup';
import { machineCosts } from './machineCosts';
import { useBomCatalog } from './useBomCatalog';

const plural = (n: number, one: string, many: string) =>
  `${n} ${n === 1 ? one : many}`;

/**
 * Live build totals for the draft BOM, shared by the Build panel and Pricing (React Query dedupes the requests).
 * Direct lines roll up locally; each distinct sub-assembly is exploded server-side at quantity 1
 * (cached per id) and scaled by its line qty.
 * ponytail: a sub-assembly whose saved tree contains this assembly inflates the preview up to the
 * CTE depth cap (10); save rejects the cycle anyway, so no client-side cycle check.
 */
export function useBuildEstimate(
  lines: EditableLine[],
  additionalCosts: AssemblyCostInput[] = [],
) {
  const catalog = useBomCatalog();
  const machineQuery = usePrinterCosts();
  const filamentQuery = useOpenFarmApiEndpointsFilamentsListFilamentsEndpoint();

  const subIds = [
    ...new Set(
      lines.filter((l) => l.targetKind === 'Assembly').map((l) => l.targetId),
    ),
  ];
  const subQueries = useQueries({
    queries: subIds.map((id) =>
      getOpenFarmApiEndpointsAssembliesExplodeAssemblyEndpointQueryOptions(id, {
        quantity: 1,
      }),
    ),
  });
  const subExplosions = new Map(
    subIds.map((id, i) => [id, subQueries[i]?.data?.data || undefined]),
  );
  const subFailed = subQueries.filter((q) => q.isError).length;
  const subPending = subQueries.some((q) => q.isPending);
  // Missing explosions drop parts from every total, so nothing reads as final until all load.
  const loading = subPending || catalog.isPending || filamentQuery.isPending;
  const failed = subFailed > 0 || catalog.isError || filamentQuery.isError;
  const incomplete = loading || failed;

  const total = rollUp(lines, subExplosions);
  const print = printTotals(total.parts, catalog.parts);
  const machine = machineCosts(
    total.parts,
    catalog.parts,
    machineQuery.data?.data.models ?? [],
  );
  const machineUnavailable =
    total.parts.size > 0 && (machineQuery.isPending || machineQuery.isError);
  const machinePartial = machine.incomplete || machineUnavailable;
  const cost = hardwareCost(total.components, catalog.components);
  const breakdown = new Map<string, number>();
  let missingAdditional = 0;
  let pricedAdditional = 0;
  const addCost = (category: string, amount: number | null | undefined) => {
    if (amount == null) {
      missingAdditional++;
      return;
    }
    pricedAdditional++;
    breakdown.set(category, (breakdown.get(category) ?? 0) + amount);
  };
  for (const [id, quantity] of total.components) {
    const item = catalog.components.find((c) => c.id === id);
    if (item?.unitCost != null) {
      const category = item.pricing?.category ?? 'Hardware';
      breakdown.set(
        category,
        (breakdown.get(category) ?? 0) + quantity * item.unitCost,
      );
    }
  }
  for (const line of additionalCosts) {
    const item = catalog.costItems.find(
      (item) => item.id === line.catalogItemId,
    );
    const consumed = usage(line, item?.pricing?.unit);
    addCost(
      item?.pricing?.category ?? 'Other',
      item?.effectiveRate == null || !(consumed > 0)
        ? null
        : consumed * item.effectiveRate,
    );
  }
  for (const line of lines.filter((line) => line.targetKind === 'Assembly')) {
    const sub = subExplosions.get(line.targetId);
    for (const inherited of sub?.productionCosts ?? [])
      addCost(
        inherited.category ?? 'Other',
        inherited.cost == null ? null : inherited.cost * line.quantity,
      );
  }
  const shipping = breakdown.get('Shipping') ?? 0;
  const physicalAndAdditional = [...breakdown.values()].reduce(
    (sum, amount) => sum + amount,
    0,
  );
  const costsPartial = cost.missingCost > 0 || missingAdditional > 0;

  const variants = new Map(
    (filamentQuery.data?.data?.filaments ?? []).flatMap((filament) =>
      (filament.variants ?? []).map(
        (variant) =>
          [
            variant.id,
            {
              name:
                [
                  [filament.vendorName, filament.material]
                    .filter(Boolean)
                    .join(' '),
                  variant.colorName,
                ]
                  .filter(Boolean)
                  .join(' · ') || 'Filament unavailable',
              colorHex: variant.colorHex,
            },
          ] as const,
      ),
    ),
  );
  const filament = filamentCost(
    total.parts,
    catalog.parts,
    filamentQuery.data?.data?.filaments ?? [],
  );
  // Failed prints waste filament and machine time, never packaging or shipping.
  const failurePercent =
    useGeneralSettings().data?.data.failureAllowancePercent ?? 0;
  const failure = (failurePercent / 100) * (filament.total + machine.total);
  const printPartial = filament.incomplete || machinePartial;
  const failureAvailable =
    total.parts.size === 0 ||
    filament.priced > 0 ||
    (!machineUnavailable &&
      (machine.depreciation.priced > 0 || machine.electricity.priced > 0));
  const totalAmount =
    filament.total + physicalAndAdditional + machine.total + failure;
  const totalPartial = printPartial || costsPartial;
  const totalAvailable =
    !incomplete &&
    (machine.depreciation.priced > 0 ||
      machine.electricity.priced > 0 ||
      filament.priced > 0 ||
      pricedAdditional > 0 ||
      total.components.size > cost.missingCost ||
      (total.parts.size === 0 && total.components.size === 0));

  const notes = [
    total.parts.size > 0 && machineQuery.isPending && 'Loading printer costs…',
    total.parts.size > 0 &&
      machineQuery.isError &&
      'Could not load printer costs. Retry to complete estimates.',
    !machineUnavailable &&
      machine.depreciation.missing > 0 &&
      'Printer depreciation incomplete. Configure model costs in Settings → Printer costs; parts also need a known estimation model and print time.',
    !machineUnavailable &&
      machine.electricity.missing > 0 &&
      'Electricity incomplete. Configure average printing power and electricity price in Settings → General (model power is in Settings → Printer costs); parts also need a known estimation model and print time.',

    (catalog.isPending || filamentQuery.isPending) && 'Loading catalog…',
    (catalog.isError || filamentQuery.isError) &&
      'Could not load catalog. Totals incomplete.',
    filament.missingSelection > 0 &&
      'Select catalog filament on parts to estimate all filament costs.',
    filament.missingPrice > 0 &&
      'Selected filament has no price. Filament cost incomplete.',
    filament.missingGrams > 0 &&
      'Filament weight estimates unavailable. Filament cost incomplete.',
    subPending && 'Loading sub-assemblies…',
    subFailed > 0 &&
      `Could not load ${plural(subFailed, 'sub-assembly', 'sub-assemblies')}. Totals incomplete.`,
    print.unestimated > 0 &&
      `${plural(print.unestimated, 'part', 'parts')} not estimated.`,
    missingAdditional > 0 && 'Cost items have missing prices or usage.',
    cost.missingCost > 0 &&
      `${plural(cost.missingCost, 'component', 'components')} without cost.`,
  ].filter((n): n is string => Boolean(n));

  return {
    catalog,
    machineQuery,
    filamentQuery,
    subQueries,
    subFailed,
    loading,
    failed,
    incomplete,
    total,
    print,
    machine,
    machineUnavailable,
    machinePartial,
    breakdown,
    shipping,
    costsPartial,
    variants,
    filament,
    failurePercent,
    failure,
    printPartial,
    failureAvailable,
    notes,
    /** Estimated total including shipping: what one finished product costs. */
    totalCost: {
      amount: totalAmount,
      partial: totalPartial,
      available: totalAvailable,
    },
  };
}

export type BuildEstimate = ReturnType<typeof useBuildEstimate>;
