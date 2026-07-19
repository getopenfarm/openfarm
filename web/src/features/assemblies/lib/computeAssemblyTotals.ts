// Pure, hook-free computation so it can be used both by the committed
// AssembliesPage list (over `assembliesStore`) and by the in-progress draft
// in AssemblyFormPage/SummarySidebar (over local component state) without
// forking the math.
import { partsStore } from '@/features/assemblies/data/mockPartsStore';
import type { Assembly } from '../data/mockAssembliesStore';

export interface AssemblyTotals {
  totalParts: number;
  estimatedTimeMinutes: number;
}

/**
 * Computes total part quantity and estimated print time for an assembly.
 *
 * Nesting is resolved exactly ONE level deep: when a component references a
 * sub-assembly, only that sub-assembly's own DIRECT part components are
 * summed (times the outer component's quantity) — any sub-assembly
 * components nested inside *that* sub-assembly are deliberately ignored
 * rather than recursed into. There's no such data in the seed set today,
 * but this keeps the function safe against future circular data.
 */
export function computeAssemblyTotals(
  assembly: Assembly,
  allAssemblies: Assembly[],
): AssemblyTotals {
  const parts = partsStore.getSnapshot();
  let totalParts = 0;
  let estimatedTimeMinutes = 0;

  for (const component of assembly.components) {
    if (component.partId) {
      const part = parts.find((p) => p.id === component.partId);
      if (!part) continue;
      totalParts += component.quantity;
      estimatedTimeMinutes += part.estimatedTimeMinutes * component.quantity;
      continue;
    }

    if (component.subAssemblyId) {
      const subAssembly = allAssemblies.find(
        (a) => a.id === component.subAssemblyId,
      );
      if (!subAssembly) continue;

      let subTotalParts = 0;
      let subEstimatedTimeMinutes = 0;
      for (const subComponent of subAssembly.components) {
        // Only direct part components count — a sub-assembly's own
        // sub-assembly components are not recursed into.
        if (!subComponent.partId) continue;
        const part = parts.find((p) => p.id === subComponent.partId);
        if (!part) continue;
        subTotalParts += subComponent.quantity;
        subEstimatedTimeMinutes +=
          part.estimatedTimeMinutes * subComponent.quantity;
      }

      totalParts += subTotalParts * component.quantity;
      estimatedTimeMinutes += subEstimatedTimeMinutes * component.quantity;
    }
  }

  return { totalParts, estimatedTimeMinutes };
}
