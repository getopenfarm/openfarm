import type { FeedReadingResponse } from '@/lib/api/generated/endpoints.schemas';

/**
 * Frontend twin of the backend's `FilamentColor.FromFeedReadings` — derives one representative
 * filament color for the fleet card's dot from a printer's multi-material units. Prefers the slot the
 * printer marks active (the one it's feeding from), else the first loaded slot, and normalizes the wire
 * value (8-char RGBA like `000000FF`, or the simulator's `#RRGGBB`) to a CSS `#RRGGBB`. Returns
 * null when nothing is loaded so the UI shows its neutral placeholder. Used to keep the grid card's
 * color live on `PrinterTelemetryUpdated`, since the fleet REST payload only carries the derived
 * value on first load.
 */
export function filamentColorFromReadings(
  readings: FeedReadingResponse[] | null | undefined,
): string | null {
  const slots = (readings ?? [])
    .filter((unit) => unit.kind === 'MultiMaterialUnit')
    .flatMap((unit) => unit.slots ?? [])
    .filter((slot) => !slot.empty);
  const chosen = slots.find((slot) => slot.active) ?? slots[0];
  return normalize(chosen?.colorHex);
}

function normalize(colorHex: string | null | undefined): string | null {
  if (!colorHex) return null;
  const digits = colorHex.replace(/^#/, '');
  if (digits.length < 6) return null;
  return `#${digits.slice(0, 6)}`;
}
