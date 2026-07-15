// Mock data for the Filament catalog page — no backend exists for filament
// tracking yet (see FilamentPage.tsx). This store is also read directly by
// the Parts feature's nearest-color-match algorithm, so keep these export
// names and shapes stable: `Filament`, `FilamentVariant`, `filamentStore`,
// `useFilaments`.
import { createMockStore } from '@/lib/mock-store/createMockStore';
import { SEED_FILAMENTS } from './mockFilament';

export interface FilamentVariant {
  id: string;
  name: string; // e.g. "Black"
  colorHex: string;
  weightsGrams: number[]; // subset of [250, 500, 750, 1000]
  profileOverrides?: Partial<Record<'0.4' | '0.6', string>>; // nozzle size -> profile id, only when overridden
}

export interface Filament {
  id: string;
  brand: string; // e.g. "Bambu Lab"
  material: string; // e.g. "PLA Basic"
  materialType: 'PLA' | 'PETG' | 'ABS' | 'ASA' | 'TPU' | 'PC';
  diameterMm: number; // 1.75 for all seed data
  profilesByNozzleSize?: Partial<Record<'0.4' | '0.6', string>>; // default profile id per nozzle size
  variants: FilamentVariant[];
}

export const filamentStore = createMockStore<Filament>(SEED_FILAMENTS);

export function useFilaments() {
  return filamentStore.useStore();
}
