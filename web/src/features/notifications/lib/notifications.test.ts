import { describe, expect, it } from 'vitest';
import {
  badgeText,
  bellLabel,
  canAssignSpool,
  findLine,
  lineLabel,
  reasonLabel,
} from './notifications';

describe('notifications helpers', () => {
  it('offers Assign spool only where a spool choice can fix the line', () => {
    expect(canAssignSpool({ reason: 'NoSpool' })).toBe(true);
    expect(canAssignSpool({ reason: 'SpoolNotLoaded' })).toBe(true);
    expect(canAssignSpool({ reason: 'NoEstimate' })).toBe(false);
    expect(canAssignSpool({ reason: 'NoProgress' })).toBe(false);
  });

  it('names each reason in terse domain words', () => {
    expect(reasonLabel({ reason: 'SpoolNotLoaded' })).toBe('Spool not loaded');
    expect(reasonLabel({ reason: 'Whatever' })).toBe('Not deducted');
  });

  it('labels the bell with the count and caps the badge', () => {
    expect(bellLabel(0)).toBe('Notifications');
    expect(bellLabel(2)).toBe('Notifications, 2 need action');
    expect(badgeText(9)).toBe('9');
    expect(badgeText(12)).toBe('9+');
  });

  it('labels a line by material and colour', () => {
    expect(lineLabel({ materialType: 'PLA', colorHex: '000000' })).toBe(
      'PLA #000000',
    );
    expect(lineLabel({ materialType: null, colorHex: '#ffffff' })).toBe(
      'Any material #ffffff',
    );
  });

  it('finds a line within its item', () => {
    const items = [
      { printId: 'p1', lines: [{ lineId: 'l1' }, { lineId: 'l2' }] },
    ];
    expect(findLine(items, 'p1', 'l2')?.line.lineId).toBe('l2');
    expect(findLine(items, 'p1', 'nope')).toBeUndefined();
    expect(findLine(items, 'p2', 'l1')).toBeUndefined();
  });
});
