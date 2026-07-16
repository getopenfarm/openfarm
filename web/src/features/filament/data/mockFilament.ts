// Seed data for the Filament catalog — no backend exists for filament
// tracking yet. `profilesByNozzleSize` / `profileOverrides` reference profile
// ids from the Profiles feature's mock catalog (src/features/profiles/data/mockProfiles.ts)
// — kept as loosely-coupled string ids rather than an import so this feature
// doesn't take a hard dependency on Profiles' internals.
import type { Filament } from './mockFilamentStore';

export const SEED_FILAMENTS: Filament[] = [
  {
    id: 'bambu-pla-basic',
    brand: 'Bambu Lab',
    material: 'PLA Basic',
    materialType: 'PLA',
    diameterMm: 1.75,
    profilesByNozzleSize: {
      '0.4': 'filament-bambu-pla-basic-x1c-04',
      '0.6': 'filament-bambu-pla-basic-x1c-06',
    },
    variants: [
      {
        id: 'bambu-pla-basic-black',
        name: 'Black',
        colorHex: '#18181b',
        weightsGrams: [250, 1000],
      },
      {
        id: 'bambu-pla-basic-white',
        name: 'White',
        colorHex: '#f4f4f5',
        weightsGrams: [250, 1000],
      },
      {
        id: 'bambu-pla-basic-blue',
        name: 'Blue',
        colorHex: '#3b82f6',
        weightsGrams: [250, 1000],
      },
      {
        id: 'bambu-pla-basic-orange',
        name: 'Orange',
        colorHex: '#f97316',
        weightsGrams: [250, 1000],
        profileOverrides: {
          '0.4': 'filament-orca-pla-high-speed-04',
        },
      },
    ],
  },
  {
    id: 'bambu-pla-matte',
    brand: 'Bambu Lab',
    material: 'PLA Matte',
    materialType: 'PLA',
    diameterMm: 1.75,
    profilesByNozzleSize: {
      '0.4': 'filament-bambu-pla-matte-x1c-04',
      '0.6': 'filament-bambu-pla-matte-x1c-06',
    },
    variants: [
      {
        id: 'bambu-pla-matte-charcoal',
        name: 'Charcoal',
        colorHex: '#3f3f46',
        weightsGrams: [250, 1000],
      },
      {
        id: 'bambu-pla-matte-ivory',
        name: 'Ivory',
        colorHex: '#fef9ef',
        weightsGrams: [250, 1000],
      },
      {
        id: 'bambu-pla-matte-sakura-pink',
        name: 'Sakura Pink',
        colorHex: '#f9a8d4',
        weightsGrams: [250, 1000],
      },
    ],
  },
  {
    id: 'bambu-petg-hf',
    brand: 'Bambu Lab',
    material: 'PETG HF',
    materialType: 'PETG',
    diameterMm: 1.75,
    profilesByNozzleSize: {
      '0.4': 'filament-bambu-petg-hf-x1c-04',
      '0.6': 'filament-bambu-petg-hf-x1c-06',
    },
    variants: [
      {
        id: 'bambu-petg-hf-gray',
        name: 'Gray',
        colorHex: '#71717a',
        weightsGrams: [500, 1000],
      },
      {
        id: 'bambu-petg-hf-green',
        name: 'Green',
        colorHex: '#22c55e',
        weightsGrams: [500, 1000],
      },
      {
        id: 'bambu-petg-hf-black',
        name: 'Black',
        colorHex: '#18181b',
        weightsGrams: [500, 1000],
      },
    ],
  },
  {
    id: 'bambu-asa',
    brand: 'Bambu Lab',
    material: 'ASA',
    materialType: 'ASA',
    diameterMm: 1.75,
    profilesByNozzleSize: {
      '0.4': 'filament-bambu-asa-x1c-04',
      '0.6': 'filament-bambu-asa-x1c-06',
    },
    variants: [
      {
        id: 'bambu-asa-orange',
        name: 'Orange',
        colorHex: '#f97316',
        weightsGrams: [500, 1000],
      },
      {
        id: 'bambu-asa-black',
        name: 'Black',
        colorHex: '#18181b',
        weightsGrams: [500, 1000],
      },
    ],
  },
  {
    id: 'polymaker-polyterra-pla',
    brand: 'Polymaker',
    material: 'PolyTerra PLA',
    materialType: 'PLA',
    diameterMm: 1.75,
    profilesByNozzleSize: {
      '0.4': 'filament-generic-pla-orca-04',
      '0.6': 'filament-generic-pla-orca-06',
    },
    variants: [
      {
        id: 'polymaker-polyterra-pla-cotton-white',
        name: 'Cotton White',
        colorHex: '#f5f5f0',
        weightsGrams: [500, 1000],
      },
      {
        id: 'polymaker-polyterra-pla-sakura',
        name: 'Sakura',
        colorHex: '#f9a8d4',
        weightsGrams: [250, 500],
      },
      {
        id: 'polymaker-polyterra-pla-sapphire',
        name: 'Sapphire',
        colorHex: '#0ea5e9',
        weightsGrams: [250, 500],
      },
    ],
  },
  {
    id: 'esun-pla-plus',
    brand: 'eSUN',
    material: 'PLA+',
    materialType: 'PLA',
    diameterMm: 1.75,
    profilesByNozzleSize: {
      '0.4': 'filament-generic-pla-orca-04',
      '0.6': 'filament-generic-pla-orca-06',
    },
    variants: [
      {
        id: 'esun-pla-plus-fire-red',
        name: 'Fire Red',
        colorHex: '#ef4444',
        weightsGrams: [250, 1000],
      },
      {
        id: 'esun-pla-plus-yellow',
        name: 'Yellow',
        colorHex: '#eab308',
        weightsGrams: [250, 1000],
      },
    ],
  },
];
