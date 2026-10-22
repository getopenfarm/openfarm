import type {
  AmsTrayResponse,
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

function rgb(hex: string): number[] {
  return [1, 3, 5].map((i) => Number.parseInt(hex.slice(i, i + 2), 16));
}

/** RFID and catalog hexes rarely match exactly, so only clearly different colors count. */
export function colorsDiffer(a?: string, b?: string): boolean {
  if (!a || !b) return false;
  const [x, y] = [rgb(a), rgb(b)];
  return Math.hypot(...x.map((v, i) => v - y[i])) > 64;
}

/** Printer sub-brands already name the type ("PLA Basic"), so never print both. */
export function detectedName(
  material?: string | null,
  subBrand?: string | null,
): string {
  if (!material) return subBrand ?? '';
  if (!subBrand) return material;
  return subBrand.toLowerCase().includes(material.toLowerCase())
    ? subBrand
    : `${material} ${subBrand}`;
}

/** Match hardware positions, never display labels or response array order. */
export function feedSlotView(
  unit: FeedUnitResponse,
  slot: FeedSlotResponse,
  amsUnits: AmsUnitResponse[],
  isReachable: boolean,
  externalSpoolTray?: AmsTrayResponse | null,
  externalPortReplacedByReloader = false,
) {
  const feedingReloader =
    unit.kind === 'AutomaticFilamentReloader' &&
    Boolean(unit.replacesFeedUnitId) &&
    unit.feedingSlotId === slot.id;
  const portTelemetry =
    (unit.kind === 'ExternalSpool' && !externalPortReplacedByReloader) ||
    feedingReloader;
  const tray = portTelemetry
    ? externalSpoolTray
    : unit.kind === 'MultiMaterialUnit' &&
        unit.unitIndex != null &&
        slot.slotPosition != null
      ? amsUnits
          .find((ams) => ams.unitIndex === unit.unitIndex)
          ?.trays?.find((t) => t.slotIndex === slot.slotPosition)
      : undefined;
  // For AMS, absence from the current snapshot means unknown, not stale REST detection.
  const useRestDetection = portTelemetry && externalSpoolTray === undefined;
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
/** The RFID tags a printer reads and how full each tagged spool reads; changes when a tagged spool comes, goes or is measured. */
export function amsRfidKey(
  units: AmsUnitResponse[],
  external: AmsTrayResponse | null | undefined,
): string {
  const trays = [
    ...units.flatMap((unit) =>
      (unit.trays ?? []).map((tray) => [unit.unitIndex, tray] as const),
    ),
    ...(external ? [['external', external] as const] : []),
  ];
  return trays
    .filter(([, tray]) => tray.rfidId)
    .map(
      ([unit, tray]) =>
        `${unit}:${tray.slotIndex}:${tray.rfidId}:${tray.remainPercent ?? ''}`,
    )
    .sort()
    .join('|');
}

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
