import { createMockStore } from '@/lib/mock-store/createMockStore';
import type { Profile } from './mockProfiles';
import { SEED_PROFILES } from './mockProfiles';

export const profilesStore = createMockStore<Profile>(SEED_PROFILES);

export function useProfiles() {
  return profilesStore.useStore();
}
