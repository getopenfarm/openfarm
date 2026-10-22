import type { VariantStockResponse } from '@/lib/api/generated/endpoints.schemas';

/** Colour stock rows added up: one filament's colours, or the whole inventory. */
export interface StockTotals {
  onHandGrams: number;
  activeSpools: number;
  value: number;
  /** Spools with grams left and no price; the value leaves them out, so it is partial. */
  unpricedSpools: number;
  usedGrams: number;
  lowColors: number;
}

export function sumStock(rows: VariantStockResponse[]): StockTotals {
  const totals: StockTotals = {
    onHandGrams: 0,
    activeSpools: 0,
    value: 0,
    unpricedSpools: 0,
    usedGrams: 0,
    lowColors: 0,
  };
  for (const row of rows) {
    totals.onHandGrams += row.onHandGrams ?? 0;
    totals.activeSpools += row.activeSpoolCount ?? 0;
    totals.value += row.stockValue ?? 0;
    totals.unpricedSpools += row.unpricedSpoolCount ?? 0;
    totals.usedGrams += row.usedLast30DaysGrams ?? 0;
    if (row.isLow) totals.lowColors += 1;
  }
  return totals;
}
