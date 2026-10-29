import { useFormContext } from 'react-hook-form';
import { z } from 'zod';
import {
  FormControl,
  FormDescription,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from '@/components/ui/form';
import { Input } from '@/components/ui/input';
import { MoneyInput } from '@/components/ui/money-input';
import { useCurrency } from '@/features/settings/generalSettings';
import { formatCost } from '@/lib/formatUnits';

const MAX_TEXT_LENGTH = 100;

/** Who sold the spools, on which order and when, as the strings the inputs hold: what a whole delivery shares. */
export const sharedPurchaseShape = {
  supplier: z.string().max(MAX_TEXT_LENGTH, 'Up to 100 characters'),
  orderNumber: z.string().max(MAX_TEXT_LENGTH, 'Up to 100 characters'),
  receivedOn: z.string(),
};

/** The purchase inputs of a spool form, as the strings they hold. Spread into the page's own schema. */
export const purchaseShape = {
  price: z
    .string()
    .refine(
      (value) => value.trim() === '' || Number(value) >= 0,
      'Must be 0 or more',
    ),
  ...sharedPurchaseShape,
};

export type PurchaseValues = z.infer<z.ZodObject<typeof purchaseShape>>;

/** The request fields for the supplier, order and date inputs: blank is unknown. */
export function sharedPurchaseRequest(values: Omit<PurchaseValues, 'price'>) {
  return {
    supplier: values.supplier.trim() || null,
    orderNumber: values.orderNumber.trim() || null,
    receivedOn: values.receivedOn || null,
  };
}

/** The request fields for the purchase inputs: blank is unknown, and a price of 0 stays 0. */
export function purchaseRequest(values: PurchaseValues) {
  return {
    price: values.price.trim() === '' ? null : Number(values.price),
    ...sharedPurchaseRequest(values),
  };
}

/**
 * What was paid, to whom, on which order and when: one compact row that wraps on narrow screens. The price is for the
 * whole spool; the per-kg figure beneath it is what compares with a filament's catalog price.
 * Reads the surrounding form, so it works inside any form that spreads `purchaseShape` (or `sharedPurchaseShape`, with
 * `withPrice` off, when each line of a delivery has its own price).
 */
export function PurchaseFields({
  spoolWeightGrams = 0,
  withPrice = true,
}: {
  /** The spool's full weight, which the price per kg is worked out against. */
  spoolWeightGrams?: number;
  withPrice?: boolean;
}) {
  const currency = useCurrency();
  const { control, watch } = useFormContext<PurchaseValues>();
  const price = withPrice ? watch('price').trim() : '';
  const pricePerKg =
    price !== '' && Number(price) >= 0 && spoolWeightGrams > 0
      ? Number(price) / (spoolWeightGrams / 1000)
      : null;

  return (
    <div
      className={
        withPrice
          ? 'grid grid-cols-2 items-start gap-4 lg:grid-cols-4'
          : 'grid grid-cols-2 items-start gap-4 lg:grid-cols-3'
      }
    >
      {withPrice && (
        <FormField
          control={control}
          name="price"
          render={({ field }) => (
            <FormItem>
              <FormLabel>Price per spool</FormLabel>
              <FormControl>
                <MoneyInput
                  type="number"
                  inputMode="decimal"
                  step="any"
                  className="tabular-nums"
                  {...field}
                />
              </FormControl>
              {pricePerKg !== null && (
                <FormDescription>
                  {formatCost(pricePerKg, currency)} per kg
                </FormDescription>
              )}
              <FormMessage />
            </FormItem>
          )}
        />
      )}
      <FormField
        control={control}
        name="supplier"
        render={({ field }) => (
          <FormItem>
            <FormLabel>Supplier</FormLabel>
            <FormControl>
              <Input {...field} />
            </FormControl>
            <FormMessage />
          </FormItem>
        )}
      />
      <FormField
        control={control}
        name="orderNumber"
        render={({ field }) => (
          <FormItem>
            <FormLabel>Order number</FormLabel>
            <FormControl>
              <Input {...field} />
            </FormControl>
            <FormMessage />
          </FormItem>
        )}
      />
      <FormField
        control={control}
        name="receivedOn"
        render={({ field }) => (
          <FormItem>
            <FormLabel>Received on</FormLabel>
            <FormControl>
              <Input type="date" {...field} />
            </FormControl>
            <FormMessage />
          </FormItem>
        )}
      />
    </div>
  );
}
