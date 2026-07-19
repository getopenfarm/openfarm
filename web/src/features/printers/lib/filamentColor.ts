import type { AmsUnitResponse } from '@/lib/api/generated/endpoints.schemas';

/**
 * Frontend twin of the backend's `FilamentColor.FromAmsUnits` — derives one representative
 * filament color for the fleet card's dot from a printer's AMS state. Prefers the tray the printer
 * marks active (the one it's feeding from), else the first loaded tray, and normalizes the wire
 * value (8-char RGBA like `000000FF`, or the simulator's `#RRGGBB`) to a CSS `#RRGGBB`. Returns
 * null when nothing is loaded so the UI shows its neutral placeholder. Used to keep the grid card's
 * color live on `PrinterTelemetryUpdated`, since the fleet REST payload only carries the derived
 * value on first load.
 */
export function filamentColorFromAms(
  amsUnits: AmsUnitResponse[] | null | undefined,
): string | null {
  const trays = (amsUnits ?? [])
    .flatMap((unit) => unit.trays ?? [])
    .filter((tray) => !tray.empty);
  const chosen = trays.find((tray) => tray.active) ?? trays[0];
  return normalize(chosen?.colorHex);
}

function normalize(colorHex: string | null | undefined): string | null {
  if (!colorHex) return null;
  const digits = colorHex.replace(/^#/, '');
  if (digits.length < 6) return null;
  return `#${digits.slice(0, 6)}`;
}
