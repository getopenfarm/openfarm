import {
  feedSlotView,
  normalizeFilamentColor,
} from '@/features/printers/lib/feedSlotView';
import type {
  FeedSlotResponse,
  FeedUnitResponse,
  FilamentResponse,
  SpoolResponse,
} from '@/lib/api/generated/endpoints.schemas';
import type { MonitoredPrinter } from './monitoring';

export interface FilamentMeter {
  key: string;
  /** Persisted Feed slot id; absent for slots synthesized from telemetry. */
  slotId?: string;
  position: number;
  location: string;
  material: string;
  color?: string;
  hasAssignedSpool: boolean;
  grams?: number;
  capacity?: number;
  printerPercent?: number | null;
  level?: number;
  source: 'recorded' | 'printer' | 'empty' | 'unknown';
  active: boolean;
  selected: boolean;
  estimated: boolean;
  standby: boolean;
  reportedEmpty: boolean;
}
export interface FilamentRail {
  kind: 'external' | 'reloader' | 'ams' | 'unknown';
  label: string;
  meters: FilamentMeter[];
  additionalUnits: string[];
  additionalSlots: number;
}

/** The backend's FeedUnitDisplay label, for a unit seen in telemetry before it is persisted: a Snapmaker's unit is its toolheads. */
function multiMaterialLabel(
  brand: string | null | undefined,
  unitIndex: number | null | undefined,
) {
  return brand === 'Snapmaker' ? 'Toolheads' : `AMS ${(unitIndex ?? 0) + 1}`;
}

/** Feed equipment is identified by topology, never material/color or array position. */
export function buildFilamentRail(
  printer: MonitoredPrinter,
  feedUnits: FeedUnitResponse[],
  spools: SpoolResponse[],
  catalog: FilamentResponse[],
): FilamentRail {
  const available = feedUnits.filter((unit) => !unit.archivedAtUtc);
  const units = available.filter(
    (unit) =>
      !(
        unit.kind === 'ExternalSpool' &&
        available.some(
          (other) =>
            other.kind === 'AutomaticFilamentReloader' &&
            other.replacesFeedUnitId === unit.id,
        )
      ),
  );
  for (const ams of printer.amsUnits ?? []) {
    if (
      !units.some(
        (unit) =>
          unit.kind === 'MultiMaterialUnit' && unit.unitIndex === ams.unitIndex,
      )
    ) {
      units.push({
        kind: 'MultiMaterialUnit',
        unitIndex: ams.unitIndex,
        displayLabel: multiMaterialLabel(printer.brand, ams.unitIndex),
      });
    }
  }
  if (
    printer.externalSpoolTray &&
    !available.some(
      (unit) =>
        unit.kind === 'ExternalSpool' ||
        (unit.kind === 'AutomaticFilamentReloader' && unit.replacesFeedUnitId),
    )
  ) {
    units.push({ kind: 'ExternalSpool', displayLabel: 'External spool' });
  }
  const groups = units
    .map((unit) => {
      const kind =
        unit.kind === 'MultiMaterialUnit'
          ? 'ams'
          : unit.kind === 'AutomaticFilamentReloader'
            ? 'reloader'
            : 'external';
      const label =
        kind === 'ams'
          ? unit.displayLabel ||
            multiMaterialLabel(printer.brand, unit.unitIndex)
          : unit.displayLabel ||
            (kind === 'reloader' ? 'Reloader' : 'External spool');
      const slots: FeedSlotResponse[] = [...(unit.feedSlots ?? [])].filter(
        (slot) => !slot.deactivatedAtUtc,
      );
      const deactivated = new Set(
        (unit.feedSlots ?? [])
          .filter((slot) => slot.deactivatedAtUtc)
          .map((slot) => slot.slotPosition),
      );
      if (kind === 'ams') {
        const telemetry = printer.amsUnits?.find(
          (ams) => ams.unitIndex === unit.unitIndex,
        );
        for (const tray of telemetry?.trays ?? []) {
          if (
            !deactivated.has(tray.slotIndex) &&
            !slots.some((slot) => slot.slotPosition === tray.slotIndex)
          )
            slots.push({ slotPosition: tray.slotIndex });
        }
        for (let position = 0; slots.length < 4 && position < 4; position++) {
          if (
            !deactivated.has(position) &&
            !slots.some((slot) => slot.slotPosition === position)
          )
            slots.push({ slotPosition: position });
        }
      }
      if (kind === 'external' && slots.length === 0)
        slots.push({ slotPosition: 0 });
      const meters: FilamentMeter[] = slots
        .sort((a, b) => (a.slotPosition ?? 0) - (b.slotPosition ?? 0))
        .map((slot, index) => {
          const view = feedSlotView(
            unit,
            slot,
            printer.amsUnits ?? [],
            printer.isReachable ?? false,
            printer.externalSpoolTray,
          );
          const current = slot.currentSpool;
          const spool = current?.id
            ? spools.find((item) => item.id === current.id)
            : undefined;
          const filament = current
            ? catalog.find((item) =>
                item.variants?.some(
                  (variant) => variant.id === current.filamentVariantId,
                ),
              )
            : undefined;
          const variant = filament?.variants?.find(
            (item) => item.id === current?.filamentVariantId,
          );
          const grams =
            spool?.remainingWeightGrams ?? current?.remainingWeightGrams;
          const capacity = spool?.spoolWeightGrams;
          const selected =
            kind === 'reloader' &&
            Boolean(slot.id && unit.feedingSlotId === slot.id);
          const validPercent =
            view.remainPercent != null &&
            Number.isFinite(view.remainPercent) &&
            view.remainPercent >= 0 &&
            view.remainPercent <= 100;
          const source =
            current && grams != null && capacity != null && capacity > 0
              ? 'recorded'
              : !current && view.empty
                ? 'empty'
                : !current && validPercent
                  ? 'printer'
                  : 'unknown';
          return {
            key:
              slot.id ??
              `${unit.id ?? `${kind}:${unit.unitIndex}`}:${slot.slotPosition ?? index}`,
            slotId: slot.id,
            position: slot.slotPosition ?? index,
            location:
              slot.displayLabel ||
              `${label} · Slot ${(slot.slotPosition ?? index) + 1}`,
            material: current
              ? [filament?.material, variant?.colorName]
                  .filter(Boolean)
                  .join(' · ') || 'Registered spool'
              : view.material ||
                (view.empty ? 'Empty slot' : 'Unregistered filament'),
            color: current
              ? normalizeFilamentColor(variant?.colorHex)
              : view.color,
            hasAssignedSpool: Boolean(current),
            grams,
            capacity,
            printerPercent: validPercent ? view.remainPercent : undefined,
            level:
              source === 'recorded'
                ? Math.max(
                    0,
                    Math.min(100, ((grams ?? 0) / (capacity ?? 1)) * 100),
                  )
                : source === 'printer'
                  ? (view.remainPercent ?? undefined)
                  : source === 'empty'
                    ? 0
                    : undefined,
            source,
            active: view.active,
            selected,
            estimated: selected && unit.feedingSelectionSource === 'Estimated',
            standby: kind === 'reloader' && !selected && Boolean(current),
            reportedEmpty: view.empty,
          };
        });
      return { unit, kind, label, meters };
    })
    .sort(
      (a, b) =>
        (a.unit.unitIndex ?? 0) - (b.unit.unitIndex ?? 0) ||
        (a.unit.id ?? '').localeCompare(b.unit.id ?? ''),
    );
  const chosen =
    groups.find((group) => group.meters.some((meter) => meter.active)) ??
    groups.find(
      (group) =>
        group.kind === 'reloader' &&
        group.meters.some((meter) => meter.selected),
    ) ??
    groups.find((group) => group.kind === 'ams') ??
    groups.find((group) => group.kind === 'external') ??
    groups[0];
  if (!chosen)
    return {
      kind: 'unknown',
      label: 'Filament',
      meters: [],
      additionalUnits: [],
      additionalSlots: 0,
    };
  let meters = chosen.meters;
  const limit = chosen.kind === 'ams' ? 4 : chosen.kind === 'reloader' ? 2 : 1;
  if (chosen.kind === 'reloader' && meters.length > 2) {
    const selected = meters.find((meter) => meter.selected || meter.active);
    const next = meters.find(
      (meter) => meter !== selected && meter.grams != null && meter.grams > 0,
    );
    meters = [selected, next].filter((meter): meter is FilamentMeter =>
      Boolean(meter),
    );
    for (const meter of chosen.meters)
      if (meters.length < 2 && !meters.includes(meter)) meters.push(meter);
    meters.sort((a, b) => a.position - b.position);
  } else if (meters.length > limit) {
    const active = meters.find((meter) => meter.active);
    meters = meters.slice(0, limit);
    if (active && !meters.includes(active)) meters[limit - 1] = active;
    meters.sort((a, b) => a.position - b.position);
  }
  return {
    kind: chosen.kind as FilamentRail['kind'],
    label: chosen.label,
    meters,
    additionalUnits: groups
      .filter((group) => group !== chosen)
      .map((group) => group.label),
    additionalSlots: chosen.meters.length - meters.length,
  };
}
