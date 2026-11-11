import { describe, expect, it } from 'vitest';
import { rollUp } from './bomRollup';
import { machineCosts } from './machineCosts';

const parts = [
  {
    id: 'p',
    unitsPerPrint: 2,
    estimatedPrintTimeMinutes: 120,
    estimationPrinterBrand: 'Bambu',
    estimationPrinterModelCode: 'P1S',
  },
  {
    id: 'q',
    unitsPerPrint: 1,
    estimatedPrintTimeMinutes: 60,
    estimationPrinterBrand: 'Snapmaker',
    estimationPrinterModelCode: 'U1',
  },
];
const models = [
  {
    brand: 'Bambu',
    modelCode: 'P1S',
    depreciationPerHour: 0.9,
    electricityPerHour: 0.075,
  },
  {
    brand: 'Snapmaker',
    modelCode: 'U1',
    depreciationPerHour: 1.5,
    electricityPerHour: 0.1,
  },
];
describe('machineCosts', () => {
  it('allocates mixed-model hours by units per print without rounding whole plates', () => {
    const result = machineCosts(
      new Map([
        ['p', 1],
        ['q', 2],
      ]),
      parts,
      models,
    );
    expect(result.depreciation.total).toBeCloseTo(3.9);
    expect(result.electricity.total).toBeCloseTo(0.275);
    expect(result.incomplete).toBe(false);
  });
  it('aggregates repeated direct and nested parts once before costing', () => {
    const rolled = rollUp(
      [
        { targetKind: 'Part', targetId: 'p', quantity: 1 },
        { targetKind: 'Assembly', targetId: 'a', quantity: 3 },
      ],
      new Map([['a', { printedParts: [{ partId: 'p', quantity: 2 }] }]]),
    );
    expect(
      machineCosts(rolled.parts, parts, models).depreciation.total,
    ).toBeCloseTo(6.3);
  });
  it('preserves separate missing-rate states and explicitly free rates', () => {
    const result = machineCosts(
      new Map([
        ['p', 1],
        ['q', 1],
      ]),
      parts,
      [
        { ...models[0], depreciationPerHour: 0, electricityPerHour: null },
        models[1],
      ],
    );
    expect(result.depreciation).toEqual({ total: 1.5, priced: 2, missing: 0 });
    expect(result.electricity).toEqual({ total: 0.1, priced: 1, missing: 1 });
    expect(result.incomplete).toBe(true);
  });
  it('does not guess a model or print time', () => {
    const result = machineCosts(
      new Map([
        ['p', 1],
        ['q', 1],
        ['missing', 1],
      ]),
      [
        { ...parts[0], estimationPrinterModelCode: null },
        { ...parts[1], estimatedPrintTimeMinutes: null },
      ],
      models,
    );
    expect(result.depreciation).toEqual({ total: 0, priced: 0, missing: 3 });
  });
  it('keeps non-printed assemblies complete without model settings', () => {
    expect(machineCosts(new Map(), [], [])).toMatchObject({
      total: 0,
      incomplete: false,
    });
  });
});
