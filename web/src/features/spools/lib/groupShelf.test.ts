import { describe, expect, it } from 'vitest';
import type { SpoolResponse } from '@/lib/api/generated/endpoints.schemas';
import { groupShelf } from './groupShelf';

const spool = (
  id: string,
  filamentVariantId: string,
  remainingWeightGrams: number,
  spoolWeightGrams = 1000,
): SpoolResponse => ({
  id,
  filamentVariantId,
  colorName: filamentVariantId,
  remainingWeightGrams,
  spoolWeightGrams,
});

describe('groupShelf', () => {
  it('groups full rolls by color and weight, largest group first, and lists partial rolls apart', () => {
    const { full, partial } = groupShelf([
      spool('w1', 'white', 1000),
      spool('b1', 'black', 1000),
      spool('b2', 'black', 1000),
      spool('b3', 'black', 640),
      spool('b4', 'black', 500, 500),
    ]);

    expect(full.map((g) => [g.key, g.spools.map((s) => s.id)])).toEqual([
      ['black:1000', ['b1', 'b2']],
      ['white:1000', ['w1']],
      ['black:500', ['b4']],
    ]);
    expect(partial.map((s) => s.id)).toEqual(['b3']);
  });
});
