export const MATERIAL_TYPES = [
  'PLA',
  'PETG',
  'ABS',
  'ASA',
  'TPU',
  'PC',
] as const;

export type MaterialType = (typeof MATERIAL_TYPES)[number];

/**
 * The material type named as a whole word in a filament name ("PETG Basic", "PLA+", "ABS-GF"),
 * or null when none or more than one matches ("PCTG", "PC-ABS"). A suggestion only; the user
 * confirms the type.
 */
export function inferMaterialType(name: string): MaterialType | null {
  const words = new Set(name.toUpperCase().split(/[^A-Z0-9]+/));
  const found = MATERIAL_TYPES.filter((type) => words.has(type));
  return found.length === 1 ? found[0] : null;
}
