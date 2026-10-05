import { describe, expect, it } from 'vitest';
import {
  amsTopologyKey,
  feedSlotView,
  normalizeFilamentColor,
} from './feedSlotView';

const unit = { kind: 'MultiMaterialUnit', unitIndex: 3 };
const slot = { slotPosition: 2 };
const ams = [
  { unitIndex: 0, trays: [{ slotIndex: 2, materialType: 'Wrong' }] },
  {
    unitIndex: 3,
    trays: [
      { slotIndex: 7, materialType: 'Wrong' },
      {
        slotIndex: 2,
        materialType: 'PLA',
        colorHex: '000000FF',
        active: true,
        remainPercent: 0,
      },
    ],
  },
];

describe('feedSlotView', () => {
  it('matches nonsequential unit and slot positions and preserves zero percent', () => {
    expect(feedSlotView(unit, slot, ams, true)).toMatchObject({
      material: 'PLA',
      color: '#000000',
      active: true,
      remainPercent: 0,
    });
  });
  it('suppresses active while offline or deactivated without discarding contents', () => {
    expect(feedSlotView(unit, slot, ams, false)).toMatchObject({
      material: 'PLA',
      active: false,
    });
    expect(
      feedSlotView(unit, { ...slot, deactivatedAtUtc: '2026-10-05' }, ams, true)
        .active,
    ).toBe(false);
  });
  it('distinguishes explicitly empty from missing telemetry and ignores stale AMS REST detection', () => {
    expect(
      feedSlotView(unit, { ...slot, detectedMaterialType: 'Stale' }, [], true),
    ).toMatchObject({ material: null, empty: false });
    expect(
      feedSlotView(
        unit,
        slot,
        [
          {
            unitIndex: 3,
            trays: [
              {
                slotIndex: 2,
                empty: true,
                materialType: 'Stale',
                active: true,
                remainPercent: 80,
              },
            ],
          },
        ],
        true,
      ),
    ).toMatchObject({
      material: null,
      empty: true,
      active: false,
      remainPercent: null,
    });
  });
  it('preserves external detection and never applies AMS telemetry to reloaders', () => {
    expect(
      feedSlotView(
        { kind: 'ExternalSpool' },
        {
          detectedMaterialType: 'PETG',
          detectedSubBrand: 'Basic',
          detectedColorHex: '#123456',
        },
        ams,
        true,
      ),
    ).toMatchObject({ material: 'PETG', subBrand: 'Basic', color: '#123456' });
    expect(
      feedSlotView(
        { ...unit, kind: 'AutomaticFilamentReloader' },
        slot,
        ams,
        true,
      ).material,
    ).toBeNull();
  });
  it('normalizes RGB/RGBA with optional hash and rejects malformed colors', () => {
    for (const color of ['123456', '#123456', '123456FF', '#123456FF'])
      expect(normalizeFilamentColor(color)).toBe('#123456');
    expect(normalizeFilamentColor('xyzxyz')).toBeUndefined();
  });
  it('topology changes only for hardware positions, not telemetry or ordering', () => {
    expect(amsTopologyKey(ams)).toBe(
      amsTopologyKey(
        [...ams].reverse().map((u) => ({
          ...u,
          trays: [...u.trays]
            .reverse()
            .map((t) => ({ ...t, remainPercent: 45 })),
        })),
      ),
    );
    expect(amsTopologyKey(ams)).not.toBe(amsTopologyKey([]));
  });
});
