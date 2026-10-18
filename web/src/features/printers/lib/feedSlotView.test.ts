import { describe, expect, it } from 'vitest';
import {
  amsTopologyKey,
  colorsDiffer,
  detectedName,
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

describe('reloader telemetry projection', () => {
  const unit = {
    kind: 'AutomaticFilamentReloader',
    replacesFeedUnitId: 'external',
    feedingSlotId: 'ch2',
  };
  const external = {
    active: true,
    empty: false,
    materialType: 'PLA',
    colorHex: '00FF00',
    remainPercent: 0,
  };
  it('projects port telemetry only onto the selected slot', () => {
    expect(feedSlotView(unit, { id: 'ch2' }, [], true, external)).toMatchObject(
      { material: 'PLA', active: true, remainPercent: 0 },
    );
    expect(feedSlotView(unit, { id: 'ch1' }, [], true, external)).toMatchObject(
      { material: null, active: false },
    );
  });
  it('keeps the selected slot separate from offline activity', () => {
    expect(
      feedSlotView(unit, { id: 'ch2' }, [], false, external),
    ).toMatchObject({ material: 'PLA', active: false });
  });
  it('does not duplicate detection on a revealed replaced port', () => {
    expect(
      feedSlotView(
        { kind: 'ExternalSpool', supersededByFeedUnitId: 'reloader' },
        { detectedMaterialType: 'PLA' },
        [],
        true,
        external,
        true,
      ),
    ).toMatchObject({ material: null, active: false });
  });
  it('does not project telemetry onto an independent reloader', () => {
    expect(
      feedSlotView(
        { ...unit, replacesFeedUnitId: null },
        { id: 'ch2' },
        [],
        true,
        external,
      ),
    ).toMatchObject({ material: null, active: false });
  });
});

describe('detectedName', () => {
  it('does not repeat the type already in the sub-brand', () => {
    expect(detectedName('PLA', 'PLA Basic')).toBe('PLA Basic');
    expect(detectedName('PETG', 'Basic')).toBe('PETG Basic');
    expect(detectedName('PLA', null)).toBe('PLA');
    expect(detectedName(null, null)).toBe('');
  });
});

describe('colorsDiffer', () => {
  it('ignores near-identical colors and flags clearly different ones', () => {
    expect(colorsDiffer('#18181b', '#000000')).toBe(false);
    expect(colorsDiffer('#000000', '#ffffff')).toBe(true);
    expect(colorsDiffer(undefined, '#ffffff')).toBe(false);
  });
});
