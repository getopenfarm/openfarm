import { describe, expect, it } from 'vitest';
import type {
  FeedUnitResponse,
  SpoolResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { buildFilamentRail } from './filamentMeters';
import type { MonitoredPrinter } from './monitoring';

const external: FeedUnitResponse = {
  id: 'ext',
  kind: 'ExternalSpool',
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
      { ...online, externalSpoolTray: { active: true } },
      [external],
      spools,
      [],
    );
    expect(rail.kind).toBe('external');
    expect(rail.meters).toHaveLength(1);
    expect(rail.meters[0].active).toBe(true);
  });
  it('uses two reloader meters and projects external telemetry only onto the selected slot', () => {
    const rail = buildFilamentRail(
      {
        ...online,
        externalSpoolTray: {
          active: true,
          remainPercent: 18,
          materialType: 'PLA',
        },
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
        amsUnits: [
          {
            unitIndex: 5,
            trays: [{ slotIndex: 3, active: true, remainPercent: 25 }],
          },
          { unitIndex: 1, trays: [{ slotIndex: 0, remainPercent: 50 }] },
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
      { ...online, amsUnits: [{ unitIndex: 4 }, { unitIndex: 2 }] },
      [],
      [],
      [],
    );
    expect(rail.label).toBe('AMS 3');
    expect(
      rail.meters.every((meter) => !meter.active && meter.source === 'unknown'),
    ).toBe(true);
  });
  it('matches persisted slots by hardware position, retains empty slots, and separates zero from unknown', () => {
    const rail = buildFilamentRail(
      {
        ...online,
        amsUnits: [
          {
            unitIndex: 2,
            trays: [
              { slotIndex: 3, remainPercent: 90 },
              { slotIndex: 0, empty: true },
            ],
          },
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
      { ...online, externalSpoolTray: { remainPercent: 80 } },
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
        amsUnits: [
          {
            unitIndex: 0,
            trays: [
              { slotIndex: 0, active: true, remainPercent: 60 },
              { slotIndex: 1, active: true },
            ],
          },
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
        amsUnits: [
          { unitIndex: 0, trays: [{ slotIndex: 1, remainPercent: 30 }] },
        ],
        externalSpoolTray: { active: true },
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
    const rail = buildFilamentRail(online, [ams], [], []);
    expect(rail.meters.map((meter) => meter.slotId)).toEqual([
      'persisted',
      undefined,
      undefined,
      undefined,
    ]);
  });
  it('leaves invalid printer percentages unknown and does not duplicate replaced-port telemetry', () => {
    const rail = buildFilamentRail(
      { ...online, externalSpoolTray: { active: true, remainPercent: -1 } },
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
