// Seed data for the Parts library — no backend exists for parts tracking
// yet. Where a part's detected color is described as "pinned" below, we
// look up the real variant id from the Filament catalog's mock store at
// module-eval time so the pin is a genuine cross-feature reference rather
// than a hardcoded guess; if the catalog doesn't have that exact
// brand/material/color (e.g. a seed changes later), we fall back to
// leaving the slot on auto-match instead of crashing.
import { filamentStore } from '@/features/filament/data/mockFilamentStore';
import type { Part, PartFolder } from './mockPartsStore';

function findVariant(brand: string, material: string, variantName: string) {
  const filament = filamentStore
    .getSnapshot()
    .find((f) => f.brand === brand && f.material === material);
  const variant = filament?.variants.find((v) => v.name === variantName);
  if (filament && variant) {
    return { filamentId: filament.id, variantId: variant.id };
  }
  return undefined;
}

const bambuPlaBasicBlack = findVariant('Bambu Lab', 'PLA Basic', 'Black');
const bambuPlaBasicBlue = findVariant('Bambu Lab', 'PLA Basic', 'Blue');
const bambuPetgHfGray = findVariant('Bambu Lab', 'PETG HF', 'Gray');

export const SEED_FOLDERS: PartFolder[] = [
  { id: 'folder-gridfinity-system', name: 'Gridfinity system' },
  { id: 'folder-keyboards', name: 'Keyboards' },
  { id: 'folder-signage', name: 'Signage' },
];

export const SEED_PARTS: Part[] = [
  {
    id: 'part-gridfinity-2x2-bin',
    name: 'Gridfinity 2×2 bin',
    folderId: 'folder-gridfinity-system',
    estimatedTimeMinutes: 80,
    weightGrams: 42,
    revision: 4,
    fileFormat: '3MF',
    slicer: 'OrcaSlicer',
    tags: ['Gridfinity', 'Storage'],
    colorSlots: [
      {
        slotIndex: 0,
        detectedColorHex: '#18181b',
        materialType: 'PLA',
        pinnedFilamentId: bambuPlaBasicBlack?.filamentId,
        pinnedVariantId: bambuPlaBasicBlack?.variantId,
      },
    ],
  },
  {
    id: 'part-phone-stand',
    name: 'Phone stand',
    folderId: null,
    estimatedTimeMinutes: 125,
    weightGrams: 58,
    revision: 3,
    fileFormat: '3MF',
    slicer: 'Bambu Studio',
    tags: [],
    colorSlots: [
      {
        slotIndex: 0,
        detectedColorHex: '#3b82f6',
        materialType: 'PLA',
        pinnedFilamentId: bambuPlaBasicBlue?.filamentId,
        pinnedVariantId: bambuPlaBasicBlue?.variantId,
      },
    ],
  },
  {
    id: 'part-cable-clip',
    name: 'Cable clip',
    folderId: null,
    estimatedTimeMinutes: 12,
    weightGrams: 6,
    revision: 2,
    fileFormat: 'STL',
    slicer: 'OrcaSlicer',
    tags: ['Cable mgmt', 'Batchable'],
    colorSlots: [
      { slotIndex: 0, detectedColorHex: '#18181b', materialType: 'PLA' },
    ],
  },
  {
    id: 'part-spur-gear',
    name: 'Spur gear',
    folderId: null,
    estimatedTimeMinutes: 48,
    weightGrams: 24,
    revision: 1,
    fileFormat: 'STEP',
    slicer: 'PrusaSlicer',
    tags: ['Mechanical'],
    colorSlots: [
      { slotIndex: 0, detectedColorHex: '#18181b', materialType: 'PLA' },
    ],
  },
  {
    id: 'part-planter-pot',
    name: 'Planter pot',
    folderId: null,
    estimatedTimeMinutes: 220,
    weightGrams: 180,
    revision: 2,
    fileFormat: '3MF',
    slicer: 'OrcaSlicer',
    tags: ['Home', 'Outdoor'],
    colorSlots: [
      { slotIndex: 0, detectedColorHex: '#22c55e', materialType: 'PETG' },
    ],
  },
  {
    id: 'part-cherry-keycap',
    name: 'Cherry keycap',
    folderId: 'folder-keyboards',
    estimatedTimeMinutes: 9,
    weightGrams: 3,
    revision: 5,
    fileFormat: 'STL',
    slicer: 'Bambu Studio',
    tags: ['Keyboard', 'Batchable'],
    colorSlots: [
      { slotIndex: 0, detectedColorHex: '#f4f4f5', materialType: 'PLA' },
      { slotIndex: 1, detectedColorHex: '#18181b', materialType: 'PLA' },
    ],
  },
  {
    id: 'part-enclosure-bracket',
    name: 'Enclosure bracket',
    folderId: null,
    estimatedTimeMinutes: 62,
    weightGrams: 36,
    revision: 1,
    fileFormat: '3MF',
    slicer: 'OrcaSlicer',
    tags: ['Printer mods'],
    colorSlots: [
      {
        slotIndex: 0,
        detectedColorHex: '#71717a',
        materialType: 'PETG',
        pinnedFilamentId: bambuPetgHfGray?.filamentId,
        pinnedVariantId: bambuPetgHfGray?.variantId,
      },
    ],
  },
  {
    id: 'part-sign-panel',
    name: 'Sign panel',
    folderId: 'folder-signage',
    estimatedTimeMinutes: 150,
    weightGrams: 120,
    revision: 2,
    fileFormat: '3MF',
    slicer: 'Bambu Studio',
    tags: ['Signage'],
    colorSlots: [
      { slotIndex: 0, detectedColorHex: '#f4f4f5', materialType: 'PLA' },
      // "Red" PLA may not exist in the Filament catalog seed — that's fine,
      // auto-match just picks the nearest available color.
      { slotIndex: 1, detectedColorHex: '#dc2626', materialType: 'PLA' },
      { slotIndex: 2, detectedColorHex: '#3b82f6', materialType: 'PLA' },
    ],
  },
];
