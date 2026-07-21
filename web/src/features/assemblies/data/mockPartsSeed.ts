// Seed data for Assemblies' own decoupled mock parts snapshot (mockPartsStore.ts) — not the
// real Part Library backend. Detected color slots below are left on auto-match (no pinned
// filament/variant id) since the real Filament catalog is backend-seeded per install and
// can't be resolved synchronously at module-eval time here.
import type { Part, PartFolder } from './mockPartsStore';

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
        pinnedFilamentId: undefined,
        pinnedVariantId: undefined,
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
        pinnedFilamentId: undefined,
        pinnedVariantId: undefined,
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
        pinnedFilamentId: undefined,
        pinnedVariantId: undefined,
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
