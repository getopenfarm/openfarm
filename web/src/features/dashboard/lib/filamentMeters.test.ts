import { describe, expect, it } from 'vitest';
import type {
  FeedReadingResponse,
  FeedSlotReadingResponse,
  FeedUnitResponse,
  SpoolResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { buildFilamentRail } from './filamentMeters';
import type { MonitoredPrinter } from './monitoring';

/** The external port's reading. */
const port = (slot: FeedSlotReadingResponse): FeedReadingResponse[] => [
  {
    kind: 'ExternalSpool',
    unitIndex: 0,
    displayLabel: 'External Spool',
    slots: [{ slotPosition: 0, ...slot }],
  },
];
/** An AMS reading with all four trays, as Bambu reports them. */
const reading = (
  unitIndex: number,
  ...reported: FeedSlotReadingResponse[]
): FeedReadingResponse => ({
  kind: 'MultiMaterialUnit',
  unitIndex,
  displayLabel: `AMS ${unitIndex + 1}`,
  slots: [0, 1, 2, 3].map((slotPosition) => ({
    slotPosition,
    ...reported.find((slot) => slot.slotPosition === slotPosition),
  })),
});

const external: FeedUnitResponse = {
  id: 'ext',
  kind: 'ExternalSpool',
  unitIndex: 0,
  feedSlots: [
    {
      id: 'external-slot',
      slotPosition: 0,
      currentSpool: { id: 's1', remainingWeightGrams: 328 },
    },
  ],
};
const spools: SpoolResponse[] = [
  { id: 's1', remainingWeightGrams: 328, spoolWeightGrams: 500 },
];
const online: MonitoredPrinter = { isReachable: true };
const reloader: FeedUnitResponse = {
  id: 'afr',
  kind: 'AutomaticFilamentReloader',
  replacesFeedUnitId: 'ext',
  feedingSlotId: 'b',
  feedingSelectionSource: 'Estimated',
  feedSlots: [
    {
      id: 'a',
      slotPosition: 0,
      currentSpool: { id: 's1', remainingWeightGrams: 328 },
    },
    {
      id: 'b',
      slotPosition: 1,
      currentSpool: { id: 's2', remainingWeightGrams: 182 },
    },
  ],
};

describe('Compact filament rail model', () => {
  it('scales an external spool using its actual capacity rather than assuming 1kg', () => {
    const rail = buildFilamentRail(online, [external], spools, []);
    expect(rail.kind).toBe('external');
    expect(rail.meters).toHaveLength(1);
    expect(rail.meters[0]).toMatchObject({ source: 'recorded', grams: 328 });
    expect(rail.meters[0].level).toBeCloseTo(65.6);
  });
  it('marks the lone external spool as the active feed when the printer reports it active', () => {
    const rail = buildFilamentRail(
      { ...online, feedReadings: port({ active: true }) },
      [external],
      spools,
      [],
    );
    expect(rail.kind).toBe('external');
    expect(rail.meters).toHaveLength(1);
    expect(rail.meters[0].active).toBe(true);
  });
  it('labels an external spool seen only in telemetry the way the server does', () => {
    const rail = buildFilamentRail(
      {
        ...online,
        feedReadings: port({ active: true, displayLabel: 'External Spool' }),
      },
      [],
      [],
      [],
    );
    expect(rail.meters.map((meter) => meter.location)).toEqual([
      'External Spool',
    ]);
  });
  it('uses two reloader meters and projects external telemetry only onto the selected slot', () => {
    const rail = buildFilamentRail(
      {
        ...online,
        feedReadings: port({
          active: true,
          remainPercent: 18,
          materialType: 'PLA',
        }),
      },
      [external, reloader],
      spools,
      [],
    );
    expect(rail.kind).toBe('reloader');
    expect(rail.meters).toHaveLength(2);
    expect(rail.meters[0]).toMatchObject({
      active: false,
      standby: true,
      printerPercent: undefined,
    });
    expect(rail.meters[1]).toMatchObject({
      selected: true,
      active: true,
      estimated: true,
      printerPercent: 18,
    });
    expect(rail.additionalUnits).toEqual([]);
  });
  it('chooses the selected and next loaded slot for reloaders with more than two slots', () => {
    const rail = buildFilamentRail(
      online,
      [
        {
          ...reloader,
          feedingSlotId: 'c',
          feedSlots: [
            ...(reloader.feedSlots ?? []),
            {
              id: 'c',
              slotPosition: 2,
              currentSpool: { remainingWeightGrams: 50 },
            },
            {
              id: 'd',
              slotPosition: 3,
              currentSpool: { remainingWeightGrams: 200 },
            },
          ],
        },
      ],
      spools,
      [],
    );
    expect(rail.meters.map((meter) => meter.key)).toEqual(['a', 'c']);
    expect(rail.additionalSlots).toBe(2);
  });
  it('shows the active AMS in a four-slot layout and identifies other equipment', () => {
    const rail = buildFilamentRail(
      {
        ...online,
        feedReadings: [
          reading(5, { slotPosition: 3, active: true, remainPercent: 25 }),
          reading(1, { slotPosition: 0, remainPercent: 50 }),
        ],
      },
      [],
      [],
      [],
    );
    expect(rail).toMatchObject({
      kind: 'ams',
      label: 'AMS 6',
      additionalUnits: ['AMS 2'],
    });
    expect(rail.meters).toHaveLength(4);
    expect(rail.meters[3]).toMatchObject({
      position: 3,
      active: true,
      level: 25,
      source: 'printer',
      grams: undefined,
    });
  });
  it('shows the lowest-index AMS when activity is unknown without inventing activity', () => {
    const rail = buildFilamentRail(
      { ...online, feedReadings: [reading(4), reading(2)] },
      [],
      [],
      [],
    );
    expect(rail.label).toBe('AMS 3');
    expect(rail.meters).toHaveLength(4);
    expect(
      rail.meters.every((meter) => !meter.active && meter.source === 'unknown'),
    ).toBe(true);
  });
  it("labels a toolchanger's unit as its toolheads, persisted or not", () => {
    const synthesized = buildFilamentRail(
      {
        ...online,
        feedReadings: [
          {
            kind: 'MultiMaterialUnit',
            unitIndex: 0,
            displayLabel: 'Toolheads',
            slots: [{ slotPosition: 1, displayLabel: 'T2' }],
          },
        ],
      },
      [],
      [],
      [],
    );
    expect(synthesized.label).toBe('Toolheads');
    expect(synthesized.meters.map((meter) => meter.location)).toEqual(['T2']);

    const persisted = buildFilamentRail(
      online,
      [
        {
          id: 'heads',
          kind: 'MultiMaterialUnit',
          unitIndex: 0,
          displayLabel: 'Toolheads',
          feedSlots: [{ id: 'h1', slotPosition: 0, displayLabel: 'T1' }],
        },
      ],
      [],
      [],
    );
    expect(persisted.label).toBe('Toolheads');
  });
  it('matches persisted slots by hardware position, retains empty slots, and separates zero from unknown', () => {
    const rail = buildFilamentRail(
      {
        ...online,
        feedReadings: [
          reading(
            2,
            { slotPosition: 3, remainPercent: 90 },
            { slotPosition: 0, empty: true },
          ),
        ],
      },
      [
        {
          id: 'ams',
          kind: 'MultiMaterialUnit',
          unitIndex: 2,
          feedSlots: [
            {
              id: 'slot3',
              slotPosition: 3,
              currentSpool: { id: 's1', remainingWeightGrams: 0 },
            },
          ],
        },
      ],
      [{ ...spools[0], remainingWeightGrams: 0 }],
      [],
    );
    expect(rail.meters.find((meter) => meter.position === 3)).toMatchObject({
      key: 'slot3',
      level: 0,
      source: 'recorded',
      printerPercent: 90,
    });
    expect(rail.meters.find((meter) => meter.position === 0)).toMatchObject({
      level: 0,
      source: 'empty',
    });
    expect(rail.meters.find((meter) => meter.position === 1)).toMatchObject({
      level: undefined,
      source: 'unknown',
    });
  });
  it('does not use a printer percentage as recorded weight when capacity is unknown', () => {
    const rail = buildFilamentRail(
      { ...online, feedReadings: port({ remainPercent: 80 }) },
      [external],
      [],
      [],
    );
    expect(rail.meters[0]).toMatchObject({
      grams: 328,
      source: 'unknown',
      level: undefined,
      printerPercent: 80,
    });
  });
  it('suppresses archived units and inactive slots and never labels offline readings active', () => {
    const rail = buildFilamentRail(
      {
        isReachable: false,
        feedReadings: [
          reading(
            0,
            { slotPosition: 0, active: true, remainPercent: 60 },
            { slotPosition: 1, active: true },
          ),
        ],
      },
      [
        { ...external, archivedAtUtc: '2026-10-09' },
        {
          kind: 'MultiMaterialUnit',
          unitIndex: 0,
          feedSlots: [
            { id: 'disabled', slotPosition: 1, deactivatedAtUtc: '2026-10-09' },
          ],
        },
      ],
      [],
      [],
    );
    expect(rail.kind).toBe('ams');
    expect(rail.meters.some((meter) => meter.position === 1)).toBe(false);
    expect(rail.meters.every((meter) => !meter.active)).toBe(true);
    expect(rail.meters[0].level).toBe(60);
  });
  it('chooses an active external feed over an installed AMS', () => {
    const rail = buildFilamentRail(
      {
        ...online,
        feedReadings: [
          reading(0, { slotPosition: 1, remainPercent: 30 }),
          ...port({ active: true }),
        ],
      },
      [external],
      spools,
      [],
    );
    expect(rail.kind).toBe('external');
    expect(rail.additionalUnits).toEqual(['AMS 1']);
  });
  it('exposes the Feed slot id only for persisted slots, not telemetry-synthesized ones', () => {
    const ams: FeedUnitResponse = {
      id: 'ams',
      kind: 'MultiMaterialUnit',
      unitIndex: 0,
      feedSlots: [{ id: 'persisted', slotPosition: 0 }],
    };
    const rail = buildFilamentRail(
      { ...online, feedReadings: [reading(0)] },
      [ams],
      [],
      [],
    );
    expect(rail.meters.map((meter) => meter.slotId)).toEqual([
      'persisted',
      undefined,
      undefined,
      undefined,
    ]);
  });
  it('uses the API slot label as the location instead of repeating the unit label', () => {
    const ams: FeedUnitResponse = {
      id: 'ams',
      kind: 'MultiMaterialUnit',
      unitIndex: 0,
      displayLabel: 'AMS 1',
      feedSlots: [
        { id: 'persisted', slotPosition: 0, displayLabel: 'AMS 1 · Slot 1' },
      ],
    };
    const rail = buildFilamentRail(
      { ...online, feedReadings: [reading(0)] },
      [ams],
      [],
      [],
    );
    // Slots without an API label fall back to unit + position.
    expect(rail.meters.map((meter) => meter.location)).toEqual([
      'AMS 1 · Slot 1',
      'AMS 1 · Slot 2',
      'AMS 1 · Slot 3',
      'AMS 1 · Slot 4',
    ]);
  });
  it('leaves invalid printer percentages unknown and does not duplicate replaced-port telemetry', () => {
    const rail = buildFilamentRail(
      { ...online, feedReadings: port({ active: true, remainPercent: -1 }) },
      [{ ...reloader, feedingSlotId: null }],
      [],
      [],
    );
    expect(
      rail.meters.every(
        (meter) => !meter.active && meter.printerPercent == null,
      ),
    ).toBe(true);
    expect(rail.additionalUnits).toEqual([]);
  });
});
