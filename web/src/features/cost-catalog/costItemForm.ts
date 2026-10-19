import { z } from 'zod';
import type {
  CatalogPricing,
  CostItemResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { categories, isService, physicalUnits, serviceUnits } from './costing';

export type PackageMeasurement = 'Quantity' | 'Dimensions';

// The form edits one price for one quantity (blank = 1), kept in packagePrice
// and packageQuantity. pricingPayload derives the API type, mode, and label.
export const initialPricing: CatalogPricing = {
  category: 'Hardware',
  unit: 'Piece',
  packageLabel: null,
  packagePrice: null,
  packageQuantity: null,
  length: null,
  lengthUnit: 'm',
  width: null,
  widthUnit: 'm',
};
const units: readonly string[] = [...physicalUnits, ...serviceUnits];
export const costItemSchema = z
  .object({
    name: z.string().trim().min(1, 'Name is required.'),
    packageMeasurement: z.enum(['Quantity', 'Dimensions']),
    pricing: z.custom<CatalogPricing>(),
  })
  .superRefine(({ pricing: p, packageMeasurement }, ctx) => {
    const error = (path: string, message: string) =>
      ctx.addIssue({ code: 'custom', path: ['pricing', path], message });
    if (!categories.includes(p.category as (typeof categories)[number]))
      error('category', 'Category is required.');
    if (!units.includes(p.unit ?? '')) error('unit', 'Unit is required.');
    if (
      p.packagePrice != null &&
      (!Number.isFinite(p.packagePrice) || p.packagePrice < 0)
    )
      error('packagePrice', 'Price must be ≥ 0.');
    if (isService(p.unit)) return;
    if (p.unit === 'SquareMetre' && packageMeasurement === 'Dimensions') {
      if (p.length == null || !Number.isFinite(p.length) || p.length <= 0)
        error('length', 'Length must be > 0.');
      if (p.width == null || !Number.isFinite(p.width) || p.width <= 0)
        error('width', 'Width must be > 0.');
    } else if (
      p.packageQuantity != null &&
      (!Number.isFinite(p.packageQuantity) ||
        p.packageQuantity <= 0 ||
        (p.unit === 'Piece' && !Number.isInteger(p.packageQuantity)))
    ) {
      error(
        'packageQuantity',
        p.unit === 'Piece'
          ? 'Quantity must be a positive integer.'
          : 'Quantity must be > 0.',
      );
    }
  });
export type CostItemFormValues = z.infer<typeof costItemSchema>;
export function formValues(item?: CostItemResponse): CostItemFormValues {
  const p = { ...initialPricing, ...item?.pricing };
  return {
    name: item?.name ?? '',
    packageMeasurement:
      p.length != null || p.width != null ? 'Dimensions' : 'Quantity',
    // A Direct row is the price of one unit.
    pricing:
      p.pricingMode === 'Package'
        ? p
        : { ...p, packagePrice: p.directRate ?? null, packageQuantity: null },
  };
}
// One derivation for the live preview and the save, so they cannot disagree.
export function pricingPayload(
  p: CatalogPricing,
  measurement: PackageMeasurement,
): CatalogPricing {
  const service = isService(p.unit);
  const sized =
    !service && p.unit === 'SquareMetre' && measurement === 'Dimensions';
  const quantity = p.packageQuantity ?? 1;
  const base = { ...p, itemType: service ? 'Service' : 'Physical' };
  if (service || (!sized && quantity === 1))
    return {
      ...base,
      pricingMode: 'Direct',
      directRate: p.packagePrice ?? null,
      packageLabel: null,
      packagePrice: null,
      packageQuantity: null,
      length: null,
      width: null,
    };
  return {
    ...base,
    pricingMode: 'Package',
    directRate: null,
    packageLabel: p.packageLabel || null,
    packageQuantity: sized ? null : quantity,
    length: sized ? p.length : null,
    width: sized ? p.width : null,
  };
}
