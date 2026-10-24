import type {
  FeedReadingResponse,
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

const words = (text: string) =>
  new Set(text.toLowerCase().split(/\s+/).filter(Boolean));

/**
 * A tag's sub-brand ("PLA Basic") and a filament's name are one product line when all the words of one are in the other:
 * "Basic PLA" and "PLA" fit "PLA Basic", "PLA Matte" does not. The server's load warning uses the same rule.
 */
export function sameLine(
  subBrand?: string | null,
  material?: string | null,
): boolean {
  if (!subBrand || !material) return true;
  const [tag, name] = [words(subBrand), words(material)];
  const within = (a: Set<string>, b: Set<string>) =>
    [...a].every((word) => b.has(word));
  return within(tag, name) || within(name, tag);
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

/** What the printer reports for one slot; positions only, never labels or array order. */
export function readingSlot(
  readings: FeedReadingResponse[] | undefined,
  kind: string | undefined,
  unitIndex: number | undefined,
  slotPosition: number | undefined,
) {
  return readings
    ?.find((unit) => unit.kind === kind && unit.unitIndex === unitIndex)
    ?.slots?.find((slot) => slot.slotPosition === slotPosition);
}

/** `readings` undefined = no telemetry yet, so a port falls back to the REST detection. */
export function feedSlotView(
  unit: FeedUnitResponse,
  slot: FeedSlotResponse,
  readings: FeedReadingResponse[] | undefined,
  isReachable: boolean,
  externalPortReplacedByReloader = false,
) {
  const feedingReloader =
    unit.kind === 'AutomaticFilamentReloader' &&
    Boolean(unit.replacesFeedUnitId) &&
    unit.feedingSlotId === slot.id;
  const portTelemetry =
    (unit.kind === 'ExternalSpool' && !externalPortReplacedByReloader) ||
    feedingReloader;
  const tray = feedingReloader
    ? // ponytail: the one external port, as the server does; the replaced port's own once a model has one per extruder
      readingSlot(readings, 'ExternalSpool', 0, 0)
    : unit.kind === 'MultiMaterialUnit' || portTelemetry
      ? readingSlot(readings, unit.kind, unit.unitIndex, slot.slotPosition)
      : undefined;
  // For AMS, absence from the current snapshot means unknown, not stale REST detection.
  const useRestDetection = portTelemetry && readings === undefined;
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
export function rfidKey(readings: FeedReadingResponse[]): string {
  return readings
    .flatMap((unit) =>
      (unit.slots ?? [])
        .filter((slot) => slot.rfidId)
        .map(
          (slot) =>
            `${unit.kind}:${unit.unitIndex}:${slot.slotPosition}:${slot.rfidId}:${slot.remainPercent ?? ''}`,
        ),
    )
    .sort()
    .join('|');
}

export function topologyKey(readings: FeedReadingResponse[]): string {
  return readings
    .map(
      (unit) =>
        `${unit.kind}:${unit.unitIndex}:${(unit.slots ?? [])
          .map((slot) => slot.slotPosition)
          .sort((a, b) => (a ?? -1) - (b ?? -1))
          .join(',')}`,
    )
    .sort()
    .join('|');
}
