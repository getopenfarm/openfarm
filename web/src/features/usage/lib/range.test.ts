import { describe, expect, it } from 'vitest';
import { lastDays, toInstants } from './range';

describe('range', () => {
  it('counts today as the last of the days', () => {
    expect(lastDays(7, new Date(2026, 9, 22, 15, 30))).toEqual({
      start: '2026-10-16',
      end: '2026-10-22',
    });
    expect(lastDays(30, new Date(2026, 2, 5))).toEqual({
      start: '2026-02-04',
      end: '2026-03-05',
    });
  });

  it('runs from the first local midnight to the midnight after the last day', () => {
    expect(toInstants({ start: '2026-10-16', end: '2026-10-22' })).toEqual({
      from: new Date(2026, 9, 16).toISOString(),
      to: new Date(2026, 9, 23).toISOString(),
    });
  });
});
