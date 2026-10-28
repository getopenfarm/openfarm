import { KpiCard } from '@/components/kpi-card/KpiCard';
import { KpiStrip } from '@/components/kpi-card/KpiStrip';
import type { VariantStockResponse } from '@/lib/api/generated/endpoints.schemas';
import { formatCost, formatWeight } from '@/lib/formatUnits';
import { pluralize } from '@/lib/pluralize';
import { sumStock } from '../lib/stock';

/**
 * On hand, value, active spools and grams used in 30 days, over the given colors' stock rows. `compact` is one bordered strip, for a
 * page whose own content leads (a filament's colors), instead of four tiles.
 */
export function StockTiles({
  stock,
  size = 'default',
}: {
  stock: VariantStockResponse[];
  size?: 'default' | 'compact';
}) {
  const totals = sumStock(stock);
  const cells = [
    {
      label: 'On hand',
      value: formatWeight(totals.onHandGrams),
      dotClassName: totals.lowColors > 0 ? 'bg-kind-amber' : undefined,
      sub:
        totals.lowColors > 0
          ? `${pluralize(totals.lowColors, 'color')} low`
          : undefined,
    },
    {
      label: 'Value',
      value: formatCost(totals.value),
      sub:
        totals.unpricedSpools > 0
          ? `Partial · ${pluralize(totals.unpricedSpools, 'spool')} unpriced`
          : undefined,
    },
    { label: 'Active spools', value: String(totals.activeSpools) },
    { label: 'Used 30 d', value: formatWeight(totals.usedGrams) },
  ];

  if (size === 'compact') return <KpiStrip cells={cells} />;

  return (
    <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
      {cells.map((cell) => (
        <KpiCard
          key={cell.label}
          label={cell.label}
          value={cell.value}
          dotClassName={cell.dotClassName}
          sub={cell.sub}
        />
      ))}
    </div>
  );
}
