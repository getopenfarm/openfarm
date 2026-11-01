import { describe, expect, it } from 'vitest';
import { feeTotals, profilePricing } from './pricing';

// Sheet H0001 of the pricing workbook: cost 4.47, Shopee 31.06% + 0.54 per order.
const shopee = { name: 'Shopee', feePercent: 31.06, fixedFee: 0.54 };
const direct = { name: 'Direct', feePercent: 0, fixedFee: 0 };
const cost = 4.47;

describe('feeTotals', () => {
  it('sums the workbook Shopee fee lines by type', () => {
    const totals = feeTotals([
      { name: 'Commission', type: 'Percent', amount: 17.28 },
      { name: 'Payment', type: 'Percent', amount: 3.78 },
      { name: 'Offsite ads', type: 'Percent', amount: 10 },
      { name: 'Listing fee', type: 'Fixed', amount: 0.54 },
    ]);
    expect(totals.feePercent).toBeCloseTo(31.06, 10);
    expect(totals.fixedFee).toBe(0.54);
  });
});

describe('profilePricing', () => {
  it('reads a fixed price like the workbook', () => {
    const r = profilePricing(cost, shopee, { mode: 'Price', value: 27.49 });
    expect(r.fees).toBeCloseTo(9.08, 2);
    expect(r.profit).toBeCloseTo(13.94, 2);
    expect(r.margin).toBeCloseTo(0.507, 3);
    expect(r.markup).toBeCloseTo(5.15, 2);
    expect(r.breakEven).toBeCloseTo(7.27, 2);
    expect(r.maxDiscount).toBeCloseTo(0.736, 3);
  });

  it('prices from margin after fees, per fee profile', () => {
    expect(
      profilePricing(cost, shopee, { mode: 'Margin', value: 45 }).price,
    ).toBe(20.93);
    expect(
      profilePricing(cost, direct, { mode: 'Margin', value: 45 }).price,
    ).toBe(8.13);
  });

  it('prices from markup on cost, the same on every fee profile', () => {
    expect(
      profilePricing(cost, shopee, { mode: 'Markup', value: 515 }).price,
    ).toBe(27.49);
  });

  it('keeps all of the price on a fee profile without fees', () => {
    const r = profilePricing(cost, direct, { mode: 'Price', value: 27.49 });
    expect(r.profit).toBeCloseTo(23.02, 2);
    expect(r.margin).toBeCloseTo(0.837, 3);
  });

  it('flags a margin the fees make unreachable', () => {
    const r = profilePricing(cost, shopee, { mode: 'Margin', value: 70 });
    expect(r.unreachable).toBe(true);
    expect(r.price).toBeNull();
  });

  it('still gives break-even without pricing', () => {
    const r = profilePricing(cost, shopee, null);
    expect(r.price).toBeNull();
    expect(r.breakEven).toBeCloseTo(7.27, 2);
  });
});
