import type {
  AmsUnitResponse,
  FeedSlotResponse,
  FeedUnitResponse,
} from '@/lib/api/generated/endpoints.schemas';

export function normalizeFilamentColor(
  color?: string | null,
): string | undefined {
  const digits = color?.replace(/^#/, '');
  return digits && /^(?:[\da-f]{6}|[\da-f]{8})$/i.test(digits)
    ? `#${digits.slice(0, 6)}`
    : undefined;
}

/** Match hardware positions, never display labels or response array order. */
export function feedSlotView(
  unit: FeedUnitResponse,
  slot: FeedSlotResponse,
  amsUnits: AmsUnitResponse[],
  isReachable: boolean,
) {
  const tray =
    unit.kind === 'MultiMaterialUnit' &&
    unit.unitIndex != null &&
    slot.slotPosition != null
      ? amsUnits
          .find((ams) => ams.unitIndex === unit.unitIndex)
          ?.trays?.find((t) => t.slotIndex === slot.slotPosition)
      : undefined;
  // For AMS, absence from the current snapshot means unknown, not stale REST detection.
  const useRestDetection = unit.kind === 'ExternalSpool';
  const material = tray?.empty
    ? null
    : (tray?.materialType ??
      (useRestDetection ? slot.detectedMaterialType : null));
  const subBrand = tray?.empty
    ? null
    : (tray?.subBrand ?? (useRestDetection ? slot.detectedSubBrand : null));
  const color = normalizeFilamentColor(
    tray?.empty
      ? null
      : (tray?.colorHex ?? (useRestDetection ? slot.detectedColorHex : null)),
  );
  return {
    material,
    subBrand,
    color,
    empty: tray?.empty === true,
    active:
      isReachable &&
      !slot.deactivatedAtUtc &&
      !tray?.empty &&
      tray?.active === true,
    remainPercent: tray?.empty ? null : tray?.remainPercent,
  };
}

/** Sorting makes response reordering irrelevant to topology refreshes. */
export function amsTopologyKey(units: AmsUnitResponse[]): string {
  return JSON.stringify(
    units
      .map((unit) => [
        unit.unitIndex,
        (unit.trays ?? [])
          .map((tray) => tray.slotIndex)
          .sort((a, b) => (a ?? -1) - (b ?? -1)),
      ])
      .sort((a, b) => Number(a[0]) - Number(b[0])),
  );
}
