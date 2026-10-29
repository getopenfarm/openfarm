export type PricingMode = 'Price' | 'Margin' | 'Markup';
export const pricingModes: PricingMode[] = ['Price', 'Margin', 'Markup'];

export interface Pricing {
  mode: PricingMode;
  value: number;
}

export interface Fee {
  name?: string;
  type?: string;
  amount?: number;
}

/** A fee profile reduced to what pricing needs: its percent fees and its fixed fees, each summed. */
export interface FeeProfile {
  name: string;
  feePercent: number;
  fixedFee: number;
}

export function feeTotals(fees: Fee[]) {
  let feePercent = 0;
  let fixedFee = 0;
  for (const fee of fees) {
    if (fee.type === 'Percent') feePercent += fee.amount ?? 0;
    else fixedFee += fee.amount ?? 0;
  }
  return { feePercent, fixedFee };
}

export interface ProfilePricing {
  /** Null when no pricing is set or the margin is unreachable. */
  price: number | null;
  /** Margin + the profile's percent fees reach 100%: no price can hit it. */
  unreachable: boolean;
  fees: number | null;
  profit: number | null;
  /** Fractions, not percents. */
  margin: number | null;
  markup: number | null;
  /** Lowest price that still covers cost and fees. */
  breakEven: number;
  /** Largest discount off `price` before a loss; 0 or less means none. */
  maxDiscount: number | null;
}

const cents = (n: number) => Math.round(n * 100) / 100;

/**
 * Prices one fee profile (docs/PRICING.md). Fees come off the sale price: a percent of it plus a fixed
 * amount per order, counted once per unit. Margin is profit after fees ÷ price; markup is on cost, before fees.
 */
export function profilePricing(
  cost: number,
  profile: FeeProfile,
  pricing: Pricing | null,
): ProfilePricing {
  const rate = profile.feePercent / 100;
  const breakEven = (cost + profile.fixedFee) / (1 - rate);
  let price: number | null = null;
  let unreachable = false;
  if (pricing?.mode === 'Price') price = pricing.value;
  else if (pricing?.mode === 'Markup')
    price = cents(cost * (1 + pricing.value / 100));
  else if (pricing?.mode === 'Margin') {
    const kept = 1 - rate - pricing.value / 100;
    if (kept > 0) price = cents((cost + profile.fixedFee) / kept);
    else unreachable = true;
  }
  if (price == null)
    return {
      price,
      unreachable,
      fees: null,
      profit: null,
      margin: null,
      markup: null,
      breakEven,
      maxDiscount: null,
    };
  const fees = price * rate + profile.fixedFee;
  const profit = price - cost - fees;
  return {
    price,
    unreachable,
    fees,
    profit,
    margin: price > 0 ? profit / price : null,
    markup: cost > 0 ? (price - cost) / cost : null,
    breakEven,
    maxDiscount: price > 0 ? 1 - breakEven / price : null,
  };
}
