import { describe, expect, it } from 'vitest';
import { hardwareCost, printTotals, rollUp } from './bomRollup';

const part = (targetId: string, quantity: number) => ({
  targetKind: 'Part' as const,
  targetId,
  quantity,
});

describe('rollUp', () => {
  it('sums a part reached via a direct line and a sub-assembly', () => {
    const result = rollUp(
      [part('p1', 2), { targetKind: 'Assembly', targetId: 'a1', quantity: 3 }],
      new Map([
        [
          'a1',
          {
            printedParts: [{ partId: 'p1', quantity: 1 }],
            nonPrintedComponents: [],
          },
        ],
      ]),
    );
    expect(result.parts.get('p1')).toBe(5);
  });

  it('multiplies sub-assembly x line qty', () => {
    const result = rollUp(
      [
        { targetKind: 'Assembly', targetId: 'a1', quantity: 2 },
        { targetKind: 'NonPrintedComponent', targetId: 'c1', quantity: 4 },
      ],
      new Map([
        [
          'a1',
          {
            printedParts: [{ partId: 'p1', quantity: 3 }],
            nonPrintedComponents: [
              { nonPrintedComponentId: 'c1', quantity: 1 },
            ],
          },
        ],
      ]),
    );
    expect(result.parts.get('p1')).toBe(6);
    expect(result.components.get('c1')).toBe(6);
  });

  it('skips a sub-assembly whose explosion has not loaded', () => {
    const result = rollUp(
      [{ targetKind: 'Assembly', targetId: 'a1', quantity: 1 }],
      new Map(),
    );
    expect(result.parts.size).toBe(0);
  });
  it('skips blank, zero, and negative draft quantities', () => {
    const result = rollUp(
      [
        { targetKind: 'Part', targetId: 'p1', quantity: 0 },
        { targetKind: 'Part', targetId: 'p2', quantity: -1 },
        { targetKind: 'NonPrintedComponent', targetId: 'c1', quantity: NaN },
      ],
      new Map(),
    );
    expect(result.parts.size).toBe(0);
    expect(result.components.size).toBe(0);
  });
});

describe('printTotals', () => {
  it('divides plate estimates by units per print without rounding', () => {
    const totals = printTotals(
      new Map([
        ['p1', 5],
        ['p2', 1],
      ]),
      [
        {
          id: 'p1',
          unitsPerPrint: 2,
          estimatedPrintTimeMinutes: 60,
          estimatedFilamentWeightGrams: 10,
        },
        {
          id: 'p2',
          unitsPerPrint: 1,
          estimatedPrintTimeMinutes: 30,
          estimatedFilamentWeightGrams: 4.5,
        },
      ],
    );
    // p1: 5 units / 2 per plate = 2.5 plates, not 3.
    expect(totals.minutes).toBe(180);
    expect(totals.grams).toBe(29.5);
    expect(totals.unestimated).toBe(0);
  });

  it('counts parts without an estimate', () => {
    const totals = printTotals(
      new Map([
        ['p1', 1],
        ['p2', 1],
      ]),
      [{ id: 'p1', estimatedPrintTimeMinutes: 10 }],
    );
    expect(totals.minutes).toBe(10);
    expect(totals.unestimated).toBe(1);
  });
});

describe('hardwareCost', () => {
  it('sums qty x cost per piece and counts components without cost', () => {
    const cost = hardwareCost(
      new Map([
        ['screw', 4],
        ['magnet', 2],
        ['label', 1],
      ]),
      [
        { id: 'screw', unitCost: 0.05 },
        { id: 'magnet', unitCost: 0.25 },
        { id: 'label', unitCost: null },
      ],
    );
    expect(cost.total).toBeCloseTo(0.7);
    expect(cost.missingCost).toBe(1);
  });
});
