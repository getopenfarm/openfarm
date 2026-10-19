import { describe, expect, it } from 'vitest';
import { filamentCost, hardwareCost, printTotals, rollUp } from './bomRollup';

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

  it('splits grams by colour, scaled by plates, and the colours add up to the total', () => {
    const totals = printTotals(
      new Map([
        ['p1', 4],
        ['p2', 1],
      ]),
      [
        {
          id: 'p1',
          unitsPerPrint: 2,
          estimatedFilamentWeightGrams: 10,
          filamentByColor: [
            { materialType: 'PLA', colorHex: '#000000', grams: 7 },
            { materialType: 'PLA', colorHex: '#FFFFFF', grams: 3.5 },
          ],
        },
        {
          id: 'p2',
          estimatedFilamentWeightGrams: 5,
          filamentByColor: [
            { materialType: 'PLA', colorHex: '#000000', grams: 5.4 },
          ],
        },
      ],
    );
    // p1: 2 plates. Black 14 + 5.4, white 7 — purge-inclusive grams, not the slicer total.
    expect(totals.colors).toEqual([
      { materialType: 'PLA', colorHex: '#000000', grams: 19.4 },
      { materialType: 'PLA', colorHex: '#FFFFFF', grams: 7 },
    ]);
    expect(totals.grams).toBeCloseTo(26.4);
  });

  it('puts grams it cannot attribute to a colour in one unknown bucket, last', () => {
    const totals = printTotals(
      new Map([
        ['p1', 1],
        ['p2', 1],
        ['p3', 1],
      ]),
      [
        // several colours, no split yet (not reprocessed)
        {
          id: 'p1',
          estimatedFilamentWeightGrams: 8,
          filamentByColor: [
            { materialType: 'PLA', colorHex: '#000000', grams: null },
            { materialType: 'PLA', colorHex: '#FFFFFF', grams: null },
          ],
        },
        // one colour, no split yet: the total is that colour's
        {
          id: 'p2',
          estimatedFilamentWeightGrams: 2,
          filamentByColor: [
            { materialType: 'PETG', colorHex: '#FF0000', grams: null },
          ],
        },
        { id: 'p3', estimatedFilamentWeightGrams: 1 },
      ],
    );
    expect(totals.colors).toEqual([
      { materialType: 'PETG', colorHex: '#FF0000', grams: 2 },
      { materialType: null, colorHex: null, grams: 9 },
    ]);
    expect(totals.grams).toBe(11);
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
  const requirement = (variant: string | null, grams: number | null = 100) => ({
    materialType: 'PLA',
    colorHex: '#000000',
    grams,
    narrowFilamentVariantId: variant,
  });

  it('scales per-print grams by quantity and units per print, then applies overrides before grouping', () => {
    const result = filamentCost(
      new Map([['p', 3]]),
      [
        {
          id: 'p',
          unitsPerPrint: 2,
          filamentByColor: [requirement('base'), requirement('special')],
        },
      ],
      prices,
    );
    expect(result.total).toBe(7.5);
    expect(result.incomplete).toBe(false);
    expect([...result.colors.values()]).toEqual([
      { total: 7.5, priced: 2, missing: 0 },
    ]);
  });

  it('keeps zero prices and falls back to aggregate weight only for one requirement', () => {
    const result = filamentCost(
      new Map([['p', 2]]),
      [
        {
          id: 'p',
          estimatedFilamentWeightGrams: 100,
          filamentByColor: [requirement('free', null)],
        },
      ],
      prices,
    );
    expect(result.total).toBe(0);
    expect(result.priced).toBe(1);
    expect(result.incomplete).toBe(false);
  });

  it('reports partial prices without guessing a product from a color', () => {
    const result = filamentCost(
      new Map([['p', 1]]),
      [
        {
          id: 'p',
          filamentByColor: [
            requirement('base'),
            requirement(null),
            requirement('missing'),
          ],
        },
      ],
      prices,
    );
    expect(result.total).toBe(2);
    expect(result.missingSelection).toBe(1);
    expect(result.missingPrice).toBe(1);
    expect(result.incomplete).toBe(true);
  });

  it('does not divide an aggregate weight across unsplit requirements', () => {
    const result = filamentCost(
      new Map([['p', 1]]),
      [
        {
          id: 'p',
          estimatedFilamentWeightGrams: 200,
          filamentByColor: [
            requirement('base', null),
            requirement('special', null),
          ],
        },
      ],
      prices,
    );
    expect(result.priced).toBe(0);
    expect(result.missingGrams).toBe(2);
    expect(result.missingSelection).toBe(0);
    expect(result.incomplete).toBe(true);
  });

  it('recomputes inherited prices when a default changes or an override is cleared', () => {
    const parts = new Map([['p', 1]]);
    const catalog = [{ id: 'p', filamentByColor: [requirement('special')] }];
    expect(filamentCost(parts, catalog, prices).total).toBe(3);
    expect(
      filamentCost(parts, catalog, [
        { pricePerKg: 40, variants: [{ id: 'special', pricePerKg: null }] },
      ]).total,
    ).toBe(4);
  });
});
