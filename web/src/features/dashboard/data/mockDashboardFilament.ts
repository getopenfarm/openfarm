// Mock data for the Dashboard's "Low filament" KPI + panel — no Filament/Spools
// backend exists yet (see CLAUDE.md: Phase 1 fleet management is live, but
// filament tracking hasn't been built). This is intentionally its OWN small
// dataset, decoupled from the Filament feature's future mock store, so the
// dashboard doesn't break when that feature lands or changes shape.
//
// Swap-ready: once a real Filament/Spools-backed low-stock query exists,
// replace this file's export with a hook call (e.g.
// `useListLowStockSpools()`) and drop this file.

export interface LowFilamentSpool {
  id: string;
  colorHex: string;
  colorName: string;
  material: string;
  location: string;
  remainingPercent: number;
}

export const LOW_FILAMENT_SPOOLS: LowFilamentSpool[] = [
  {
    id: 'mock-spool-1',
    colorHex: '#47bfff',
    colorName: 'Blue PLA Basic',
    material: 'PLA',
    location: 'X1C-01 · AMS 2',
    remainingPercent: 18,
  },
  {
    id: 'mock-spool-2',
    colorHex: '#1c1c1c',
    colorName: 'Black PETG HF',
    material: 'PETG',
    location: 'P1S-03 · AMS 1',
    remainingPercent: 12,
  },
  {
    id: 'mock-spool-3',
    colorHex: '#f1eefa',
    colorName: 'White PLA Matte',
    material: 'PLA',
    location: 'Storage shelf B',
    remainingPercent: 22,
  },
];
