// Nearest-color-match algorithm for Parts' detected color slots — this is
// real matching logic (not cosmetic), ported faithfully from the mockup:
// each detected color from a sliced file gets matched to the closest
// available Filament catalog variant of the same material type, so a part
// can be auto-assigned a printable filament without the user hand-picking
// one for every slot. Callers fetch the catalog via the real API (see
// useFilamentCatalog) and pass it in — these stay pure/testable functions.
import type { FilamentResponse } from '@/lib/api/generated/endpoints.schemas';
import type { DetectedColorSlot } from '../data/mockPartsStore';

export function hexToRgb(hex: string): { r: number; g: number; b: number } {
  const normalized = hex.replace('#', '');
  const bigint = Number.parseInt(normalized, 16);
  return {
    r: (bigint >> 16) & 255,
    g: (bigint >> 8) & 255,
    b: bigint & 255,
  };
}

export function colorDistance(a: string, b: string): number {
  const rgbA = hexToRgb(a);
  const rgbB = hexToRgb(b);
  return Math.sqrt(
    (rgbA.r - rgbB.r) ** 2 + (rgbA.g - rgbB.g) ** 2 + (rgbA.b - rgbB.b) ** 2,
  );
}

export interface AutoMatchResult {
  filamentId: string;
  variantId: string;
  distance: number;
}

export function autoMatchColor(
  targetHex: string,
  materialType: string,
  filaments: FilamentResponse[],
): AutoMatchResult | null {
  let best: AutoMatchResult | null = null;

  for (const filament of filaments) {
    if (filament.materialType !== materialType || !filament.id) continue;
    for (const variant of filament.variants ?? []) {
      if (!variant.id || !variant.colorHex) continue;
      const distance = colorDistance(targetHex, variant.colorHex);
      if (!best || distance < best.distance) {
        best = { filamentId: filament.id, variantId: variant.id, distance };
      }
    }
  }

  return best;
}

function findFilamentAndVariant(
  filaments: FilamentResponse[],
  filamentId?: string,
  variantId?: string,
) {
  const filament = filaments.find((f) => f.id === filamentId);
  const variant = filament?.variants?.find((v) => v.id === variantId);
  return filament && variant ? { filament, variant } : undefined;
}

/** Resolves the color a slot will actually print with: the pinned variant's
 * color when the user specified one, otherwise the nearest auto-match, and
 * falling back to the raw detected color if no catalog match exists at all
 * (e.g. no filaments of that material type are stocked yet). Shared by the
 * Parts grid/table/edit views and the Print modal so they never disagree. */
export function resolveSlotDisplayColor(
  slot: DetectedColorSlot,
  filaments: FilamentResponse[],
): string {
  if (slot.pinnedFilamentId && slot.pinnedVariantId) {
    const pinned = findFilamentAndVariant(
      filaments,
      slot.pinnedFilamentId,
      slot.pinnedVariantId,
    );
    if (pinned?.variant.colorHex) return pinned.variant.colorHex;
  }

  const match = autoMatchColor(
    slot.detectedColorHex,
    slot.materialType,
    filaments,
  );
  if (match) {
    const resolved = findFilamentAndVariant(
      filaments,
      match.filamentId,
      match.variantId,
    );
    if (resolved?.variant.colorHex) return resolved.variant.colorHex;
  }

  return slot.detectedColorHex;
}
