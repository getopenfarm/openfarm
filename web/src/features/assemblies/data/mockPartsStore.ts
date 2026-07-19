// Assemblies' own frozen mock snapshot of "parts" — the real Part Library feature
// (web/src/features/parts/) now has a real backend and no longer has mock data of its own.
// Assemblies (Phase 6) hasn't been wired to the real Parts API yet, so its part picker +
// time/weight totals keep reading this local, decoupled copy until that phase lands. Keep
// these export names and shapes stable: `Part`, `DetectedColorSlot`, `PartFolder`,
// `partsStore`, `useParts`, `FOLDERS`.
import { createMockStore } from '@/lib/mock-store/createMockStore';
import { SEED_FOLDERS, SEED_PARTS } from './mockPartsSeed';

export interface DetectedColorSlot {
  slotIndex: number;
  detectedColorHex: string;
  materialType: 'PLA' | 'PETG' | 'ABS' | 'ASA' | 'TPU' | 'PC';
  // undefined = auto-match (nearest catalog color); when set, user has pinned a specific variant
  pinnedFilamentId?: string;
  pinnedVariantId?: string;
}

export interface Part {
  id: string;
  name: string;
  folderId: string | null; // null = loose part at library root
  estimatedTimeMinutes: number;
  weightGrams: number;
  revision: number; // "v1", "v2" etc — stored as a plain number, rendered "v{n}"
  fileFormat: '3MF' | 'STL' | 'STEP' | 'GCODE' | 'OBJ';
  slicer: 'Bambu Studio' | 'OrcaSlicer' | 'PrusaSlicer' | 'Cura';
  tags: string[];
  colorSlots: DetectedColorSlot[];
}

export interface PartFolder {
  id: string;
  name: string;
}

export const partsStore = createMockStore<Part>(SEED_PARTS);

export function useParts() {
  return partsStore.useStore();
}

// Folders aren't a separate route/entity per the mockup, but "New folder"
// needs to actually persist for the session (consistent with the rest of
// the app's mock-store pattern), so folders live in their own mock store.
// `FOLDERS` is kept as a plain seed array export too, for any consumer that
// just wants the fixed seed list synchronously.
export const FOLDERS: PartFolder[] = SEED_FOLDERS;

export const foldersStore = createMockStore<PartFolder>(SEED_FOLDERS);

export function useFolders() {
  return foldersStore.useStore();
}
