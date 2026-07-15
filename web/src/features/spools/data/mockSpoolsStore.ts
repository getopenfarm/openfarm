import { createMockStore } from '@/lib/mock-store/createMockStore';
import type { Spool } from './mockSpools';
import { SEED_SPOOLS } from './mockSpools';

export const spoolsStore = createMockStore<Spool>(SEED_SPOOLS);

export function useSpools() {
  return spoolsStore.useStore();
}
