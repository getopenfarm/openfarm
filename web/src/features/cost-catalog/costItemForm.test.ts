import { describe, expect, it } from 'vitest';
import type {
  CatalogPricing,
  CostItemResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { formValues, initialPricing, pricingPayload } from './costItemForm';

const pricing = (p: CatalogPricing): CatalogPricing => ({
  ...initialPricing,
  ...p,
});

describe('pricingPayload', () => {
  it('sends a price for one unit as a direct rate', () => {
    const payload = pricingPayload(pricing({ packagePrice: 0.3 }), 'Quantity');
    expect(payload).toMatchObject({
      itemType: 'Physical',
      pricingMode: 'Direct',
      directRate: 0.3,
      packagePrice: null,
      packageQuantity: null,
      packageLabel: null,
    });
    expect(
      pricingPayload(
        pricing({ packagePrice: 0.3, packageQuantity: 1 }),
        'Quantity',
      ).pricingMode,
    ).toBe('Direct');
  });

  it('sends a price for a larger quantity as a package', () => {
    expect(
      pricingPayload(
        pricing({ packagePrice: 8, packageQuantity: 200 }),
        'Quantity',
      ),
    ).toMatchObject({
      pricingMode: 'Package',
      directRate: null,
      packagePrice: 8,
      packageQuantity: 200,
      packageLabel: null,
    });
  });

  it('sends length and width, not a quantity, when sized', () => {
    const sized = pricing({
      unit: 'SquareMetre',
      packagePrice: 40,
      packageQuantity: 5,
      length: 100,
      width: 100,
      widthUnit: 'cm',
    });
    expect(pricingPayload(sized, 'Dimensions')).toMatchObject({
      pricingMode: 'Package',
      packageQuantity: null,
      length: 100,
      width: 100,
      widthUnit: 'cm',
    });
    expect(pricingPayload(sized, 'Quantity')).toMatchObject({
      packageQuantity: 5,
      length: null,
      width: null,
    });
  });

  it('derives the service type from the unit and ignores quantity and size', () => {
    expect(
      pricingPayload(
        pricing({ unit: 'Hour', packagePrice: 12, packageQuantity: 5 }),
        'Quantity',
      ),
    ).toMatchObject({
      itemType: 'Service',
      pricingMode: 'Direct',
      directRate: 12,
      packageQuantity: null,
    });
  });

  it('keeps a legacy package label on packages only', () => {
    const legacy = { packagePrice: 8, packageLabel: 'box' };
    expect(
      pricingPayload(pricing({ ...legacy, packageQuantity: 200 }), 'Quantity')
        .packageLabel,
    ).toBe('box');
    expect(pricingPayload(pricing(legacy), 'Quantity').packageLabel).toBeNull();
  });

  it('keeps a zero price distinct from a blank price', () => {
    expect(
      pricingPayload(pricing({ packagePrice: 0 }), 'Quantity'),
    ).toMatchObject({ directRate: 0 });
    expect(pricingPayload(pricing({}), 'Quantity').directRate).toBeNull();
  });
});

describe('formValues', () => {
  it('loads a direct row as the price of one unit', () => {
    const values = formValues({
      id: 'bag',
      name: 'Courier bag',
      pricing: { pricingMode: 'Direct', unit: 'Piece', directRate: 0.3 },
    } as CostItemResponse);
    expect(values.pricing).toMatchObject({
      packagePrice: 0.3,
      packageQuantity: null,
    });
    expect(values.packageMeasurement).toBe('Quantity');
  });

  it('loads a package row and opens dimensions in size mode', () => {
    const values = formValues({
      id: 'wrap',
      name: 'Bubble wrap',
      pricing: {
        pricingMode: 'Package',
        unit: 'SquareMetre',
        packagePrice: 40,
        length: 100,
        width: 1,
      },
    } as CostItemResponse);
    expect(values.pricing).toMatchObject({ packagePrice: 40, length: 100 });
    expect(values.packageMeasurement).toBe('Dimensions');
  });
});
