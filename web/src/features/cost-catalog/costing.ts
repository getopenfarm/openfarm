import type {
  AssemblyCostInput,
  CatalogPricing,
} from '@/lib/api/generated/endpoints.schemas';

export const categories = [
  'Hardware',
  'Packaging',
  'Consumables',
  'Labour',
  'Shipping',
  'Other',
] as const;
export const physicalUnits = [
  'Piece',
  'Metre',
  'SquareMetre',
  'Gram',
  'Millilitre',
] as const;
export const serviceUnits = ['Hour', 'Application'] as const;
// A unit decides the item type: hours and applications are services.
export const isService = (unit?: string | null) =>
  (serviceUnits as readonly string[]).includes(unit ?? '');
// Labor and shipping are services; everything else starts as pieces.
export const defaultUnit = (category?: string | null) =>
  category === 'Labour'
    ? 'Hour'
    : category === 'Shipping'
      ? 'Application'
      : 'Piece';
export const unitLabels: Record<string, string> = {
  Piece: 'piece',
  Metre: 'm',
  SquareMetre: 'm²',
  Gram: 'g',
  Millilitre: 'ml',
  Application: 'product',
  Hour: 'hour',
};
export const metres = (value: number, unit?: string | null) =>
  value / (unit === 'mm' ? 1000 : unit === 'cm' ? 100 : 1);
export const area = (
  length: number,
  lengthUnit: string | null | undefined,
  width: number,
  widthUnit: string | null | undefined,
) => metres(length, lengthUnit) * metres(width, widthUnit);
export const packageQuantity = (p: CatalogPricing) =>
  p.length != null && p.width != null
    ? area(p.length, p.lengthUnit, p.width, p.widthUnit)
    : p.packageQuantity;
export function effectiveRate(p: CatalogPricing): number | null {
  if (p.pricingMode !== 'Package') return p.directRate ?? null;
  const quantity = packageQuantity(p);
  return p.packagePrice != null && quantity != null && quantity > 0
    ? p.packagePrice / quantity
    : null;
}
export function usage(line: AssemblyCostInput, unit?: string | null) {
  const quantity =
    line.inputMode === 'Dimensions'
      ? area(
          line.length ?? 0,
          line.lengthUnit,
          line.width ?? 0,
          line.widthUnit,
        ) * (line.pieces ?? 1)
      : (line.quantity ?? 0);
  return unit === 'Hour' ? quantity / 60 : quantity;
}
export function formatRate(
  value: number | null | undefined,
  currency = 'USD',
): string {
  if (value == null) return 'Unknown';
  if (value !== 0 && Math.abs(value) < 1e-20)
    return `${currency} ${value.toExponential(6)}`;
  const digits =
    value === 0
      ? 2
      : Math.min(20, Math.max(2, 9 - Math.floor(Math.log10(Math.abs(value)))));
  return `${currency} ${new Intl.NumberFormat('en', {
    minimumFractionDigits: 2,
    maximumFractionDigits: digits,
  }).format(value)}`;
}
export const formatQuantity = (value: number) =>
  new Intl.NumberFormat('en', { maximumSignificantDigits: 10 }).format(value);
export const quantityLabel = (quantity: number, unit?: string | null) =>
  `${formatQuantity(quantity)} ${unit === 'Piece' && quantity !== 1 ? 'pieces' : unitLabels[unit ?? 'Piece']}`;

export const categoryLabel = (value: string) =>
  value === 'Labour' ? 'Labor' : value;
export const unitNames: Record<string, string> = {
  Piece: 'Pieces',
  Metre: 'Length (m)',
  SquareMetre: 'Area (m²)',
  Gram: 'Weight (g)',
  Millilitre: 'Volume (ml)',
  Application: 'Per product',
  Hour: 'Per hour',
};
