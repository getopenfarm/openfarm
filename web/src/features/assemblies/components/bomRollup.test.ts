import { describe, expect, it } from 'vitest';
import { filamentCost, hardwareCost, rollUp } from './bomRollup';

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

describe('filamentCost', () => {
  const prices = [
    {
      pricePerKg: 20,
      variants: [
        { id: 'base' },
        { id: 'special', pricePerKg: 30 },
        { id: 'free', pricePerKg: 0 },
      ],
    },
  ];
  it('prices the server-scaled rows using variant overrides before grouping', () => {
    const result = filamentCost(
      [
        { variantId: 'base', grams: 150 },
        { variantId: 'special', grams: 150 },
      ],
      prices,
    );
    expect(result.total).toBe(7.5);
    expect(result.incomplete).toBe(false);
  });
  it('preserves zero prices', () => {
    const result = filamentCost([{ variantId: 'free', grams: 200 }], prices);
    expect(result.total).toBe(0);
    expect(result.priced).toBe(1);
    expect(result.incomplete).toBe(false);
  });
  it('reports missing selection, pricing, and weight independently', () => {
    const result = filamentCost(
      [
        { variantId: 'base', grams: 100 },
        { grams: 100 },
        { variantId: 'missing', grams: 100 },
        { variantId: 'special', grams: null },
      ],
      prices,
    );
    expect(result.total).toBe(2);
    expect(result.missingSelection).toBe(1);
    expect(result.missingPrice).toBe(1);
    expect(result.missingGrams).toBe(1);
    expect(result.incomplete).toBe(true);
  });
  it('preserves partial color completeness from the server', () => {
    const result = filamentCost(
      [{ variantId: 'base', grams: 100, missing: 1 }],
      prices,
    );
    expect(result.total).toBe(2);
    expect(result.incomplete).toBe(true);
  });
  it('recomputes inherited pricing when an override is cleared', () => {
    expect(
      filamentCost(
        [{ variantId: 'special', grams: 100 }],
        [{ pricePerKg: 40, variants: [{ id: 'special', pricePerKg: null }] }],
      ).total,
    ).toBe(4);
  });
});
