// Mock data for the Spools page — physical filament inventory (distinct from the
// future "Filament" catalog of brand/material/color combos). No backend exists
// for spool tracking yet; this stands in for it until Phase 1+ wires up a real
// spool/AMS-slot model.

export type SpoolVendor = 'Bambu' | 'Polymaker' | 'eSUN';

export type SpoolState = 'using' | 'storage' | 'finished';

export interface Spool {
  id: string;
  colorHex: string;
  colorName: string;
  material: string;
  vendor: SpoolVendor;
  remainingGrams: number;
  totalGrams: number;
  state: SpoolState;
  /** Printer + AMS slot for spools currently loaded, e.g. "X1C-01 · AMS 1". Null
   * for spools sitting in storage or fully consumed. */
  location: string | null;
}

export const SEED_SPOOLS: Spool[] = [
  {
    id: 'spool-01',
    colorHex: '#0b0b0b',
    colorName: 'Black',
    material: 'PLA Basic',
    vendor: 'Bambu',
    remainingGrams: 820,
    totalGrams: 1000,
    state: 'using',
    location: 'X1C-01 · AMS 1',
  },
  {
    id: 'spool-02',
    colorHex: '#f5f5f0',
    colorName: 'White',
    material: 'PLA Basic',
    vendor: 'Bambu',
    remainingGrams: 140,
    totalGrams: 1000,
    state: 'using',
    location: 'X1C-01 · AMS 2',
  },
  {
    id: 'spool-03',
    colorHex: '#ff6a13',
    colorName: 'Orange',
    material: 'PETG HF',
    vendor: 'Bambu',
    remainingGrams: 350,
    totalGrams: 1000,
    state: 'using',
    location: 'P1S-02 · AMS 1',
  },
  {
    id: 'spool-04',
    colorHex: '#8f8f8f',
    colorName: 'Grey',
    material: 'PLA Matte',
    vendor: 'Bambu',
    remainingGrams: 1000,
    totalGrams: 1000,
    state: 'storage',
    location: null,
  },
  {
    id: 'spool-05',
    colorHex: '#101010',
    colorName: 'Black',
    material: 'ASA',
    vendor: 'Bambu',
    remainingGrams: 610,
    totalGrams: 1000,
    state: 'storage',
    location: null,
  },
  {
    id: 'spool-06',
    colorHex: '#1e5fd9',
    colorName: 'Blue',
    material: 'PolyLite PLA',
    vendor: 'Polymaker',
    remainingGrams: 95,
    totalGrams: 750,
    state: 'using',
    location: 'X1C-03 · AMS 1',
  },
  {
    id: 'spool-07',
    colorHex: '#1f8a3d',
    colorName: 'Green',
    material: 'PolyTerra PLA',
    vendor: 'Polymaker',
    remainingGrams: 500,
    totalGrams: 500,
    state: 'storage',
    location: null,
  },
  {
    id: 'spool-08',
    colorHex: '#c81e2c',
    colorName: 'Red',
    material: 'PolyMax PLA',
    vendor: 'Polymaker',
    remainingGrams: 0,
    totalGrams: 500,
    state: 'finished',
    location: null,
  },
  {
    id: 'spool-09',
    colorHex: '#e8e0d0',
    colorName: 'Natural',
    material: 'PC-Plus',
    vendor: 'Polymaker',
    remainingGrams: 300,
    totalGrams: 1000,
    state: 'storage',
    location: null,
  },
  {
    id: 'spool-10',
    colorHex: '#c0c0c8',
    colorName: 'Silver',
    material: 'PLA+',
    vendor: 'eSUN',
    remainingGrams: 210,
    totalGrams: 1000,
    state: 'using',
    location: 'P1S-04 · AMS 2',
  },
  {
    id: 'spool-11',
    colorHex: '#161616',
    colorName: 'Black',
    material: 'PETG',
    vendor: 'eSUN',
    remainingGrams: 680,
    totalGrams: 1000,
    state: 'storage',
    location: null,
  },
  {
    id: 'spool-12',
    colorHex: '#f2c200',
    colorName: 'Yellow',
    material: 'ABS+',
    vendor: 'eSUN',
    remainingGrams: 430,
    totalGrams: 1000,
    state: 'using',
    location: 'P1S-04 · AMS 1',
  },
];
