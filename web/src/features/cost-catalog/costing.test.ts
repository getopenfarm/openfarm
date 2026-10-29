import { describe, expect, it } from 'vitest';
import {
  defaultUnit,
  effectiveRate,
  formatRate,
  isService,
  packageQuantity,
  quantityLabel,
  usage,
} from './costing';

describe('unit helpers', () => {
  it('treats hours and products as services', () => {
    expect(isService('Hour')).toBe(true);
    expect(isService('Application')).toBe(true);
    expect(isService('Gram')).toBe(false);
    expect(isService(null)).toBe(false);
  });
  it('defaults the unit from the category', () => {
    expect(defaultUnit('Labour')).toBe('Hour');
    expect(defaultUnit('Shipping')).toBe('Application');
    expect(defaultUnit('Packaging')).toBe('Piece');
    expect(defaultUnit(undefined)).toBe('Piece');
  });
  it('pluralizes only pieces', () => {
    expect(quantityLabel(200, 'Piece')).toBe('200 pieces');
    expect(quantityLabel(1, 'Piece')).toBe('1 piece');
    expect(quantityLabel(100, 'SquareMetre')).toBe('100 m²');
  });
});

describe('measured material estimates', () => {
  it('prices the roll and cut dimensions in mixed units', () => {
    const pricing = {
      unit: 'SquareMetre',
      pricingMode: 'Package',
      packagePrice: 40,
      length: 100,
      lengthUnit: 'm',
      width: 100,
      widthUnit: 'cm',
    };
    const cut = {
      inputMode: 'Dimensions',
      length: 50,
      lengthUnit: 'cm',
      width: 200,
      widthUnit: 'mm',
      pieces: 1,
    };
    expect(packageQuantity(pricing)).toBe(100);
    expect(effectiveRate(pricing)).toBe(0.4);
    expect(usage(cut, 'SquareMetre')).toBe(0.1);
    expect(
      usage(cut, 'SquareMetre') * (effectiveRate(pricing) ?? 0),
    ).toBeCloseTo(0.04);
    expect(usage({ ...cut, pieces: 3 }, 'SquareMetre')).toBeCloseTo(0.3);
  });
  it('preserves zero and unknown prices and formats small nonzero rates', () => {
    expect(
      effectiveRate({
        pricingMode: 'Package',
        packageQuantity: 100,
        packagePrice: 0,
      }),
    ).toBe(0);
    expect(
      effectiveRate({
        pricingMode: 'Package',
        packageQuantity: 100,
        packagePrice: null,
      }),
    ).toBeNull();
    expect(formatRate(0.00001)).toBe('USD 0.00001');
    expect(formatRate(0.00001, 'MYR')).toBe('MYR 0.00001');
    expect(formatRate(0, 'JPY')).toBe('JPY 0.00');
    expect(formatRate(1e-24)).not.toBe('0.00');
    expect(usage({ quantity: 6 }, 'Hour') * 20).toBe(2);
  });
});
