import type { CatalogPricing } from '@/lib/api/generated/endpoints.schemas';
import {
  effectiveRate,
  formatRate,
  packageQuantity,
  quantityLabel,
  unitLabels,
} from './costing';

// Cost per unit, with the purchase it comes from when the price covers a pack.
export function CostPricingSummary({ pricing }: { pricing: CatalogPricing }) {
  const rate = effectiveRate(pricing);
  const validDimensions =
    (pricing.length == null && pricing.width == null) ||
    ((pricing.length ?? 0) > 0 && (pricing.width ?? 0) > 0);
  const validRate =
    rate != null && Number.isFinite(rate) && rate >= 0 && validDimensions;
  const quantity = packageQuantity(pricing);
  return (
    <p
      className="flex flex-wrap items-baseline gap-x-3 text-sm"
      aria-live="polite"
    >
      <span className="text-muted-foreground">Cost</span>
      {validRate ? (
        <>
          <span className="font-medium tabular-nums">
            {formatRate(rate)} / {unitLabels[pricing.unit ?? 'Piece']}
          </span>
          {pricing.pricingMode === 'Package' && (
            <span className="text-muted-foreground tabular-nums">
              {formatRate(pricing.packagePrice)} for{' '}
              {quantityLabel(quantity ?? 0, pricing.unit)}
            </span>
          )}
        </>
      ) : (
        <span className="text-muted-foreground">Not priced</span>
      )}
    </p>
  );
}
