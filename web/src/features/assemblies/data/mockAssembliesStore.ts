// Mock data for the Assemblies (bills-of-materials) feature — no backend
// exists yet. This store is read directly by AssembliesPage, AssemblyFormPage,
// and computeAssemblyTotals, so keep these export names and shapes stable:
// `AssemblyComponent`, `Assembly`, `assembliesStore`, `useAssemblies`.
import { createMockStore } from '@/lib/mock-store/createMockStore';
import { SEED_ASSEMBLIES } from './mockAssemblies';

export interface AssemblyComponent {
  id: string;
  // References a Part in partsStore — set when this component IS a part.
  partId?: string;
  // References another Assembly — set when this component is a nested
  // sub-assembly. Exactly one of partId/subAssemblyId is set, never both.
  subAssemblyId?: string;
  quantity: number;
}

export interface Assembly {
  id: string;
  name: string;
  description: string;
  components: AssemblyComponent[];
}

export const assembliesStore = createMockStore<Assembly>(SEED_ASSEMBLIES);

export function useAssemblies() {
  return assembliesStore.useStore();
}
