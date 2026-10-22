import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { useNavigate, useSearchParams } from 'react-router';
import { toast } from 'sonner';
import { z } from 'zod';
import { BackLink } from '@/components/back-link/BackLink';
import { FormFooter } from '@/components/form-footer/FormFooter';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { FieldGroup, FieldLegend, FieldSet } from '@/components/ui/field';
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from '@/components/ui/form';
import { Input } from '@/components/ui/input';
import { Separator } from '@/components/ui/separator';
import { Spinner } from '@/components/ui/spinner';
import { Switch } from '@/components/ui/switch';
import { useFilamentCatalog } from '@/features/filament/data/useFilamentCatalog';
import { useUnsavedChangesPrompt } from '@/hooks/useUnsavedChangesPrompt';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import {
  getOpenFarmApiEndpointsSpoolsListSpoolsEndpointQueryKey,
  useOpenFarmApiEndpointsSpoolsRegisterSpoolEndpoint,
} from '@/lib/api/generated/spools/spools';
import { formatCost, formatWeight } from '@/lib/formatUnits';
import { pluralize } from '@/lib/pluralize';
import {
  NO_PURCHASE,
  PurchaseFields,
  purchaseRequest,
  purchaseShape,
} from './components/PurchaseFields';
import { QuantityStepper } from './components/QuantityStepper';
import { type FilamentOption, SpoolLabel } from './components/SpoolLabel';
import { UnitInput } from './components/UnitInput';

const MAX_QUANTITY = 50;

const schema = z
  .object({
    filamentVariantId: z.string().min(1, 'Choose a color'),
    quantity: z
      .string()
      .refine(
        (value) =>
          value.trim() === '' ||
          (Number.isInteger(Number(value)) &&
            Number(value) >= 1 &&
            Number(value) <= MAX_QUANTITY),
        `Enter a whole number from 1 to ${MAX_QUANTITY}`,
      ),
    spoolWeightGrams: z
      .string()
      .refine((value) => Number(value) > 0, 'Must be greater than 0'),
    label: z.string(),
    partlyUsed: z.boolean(),
    remainingWeightGrams: z.string(),
    ...purchaseShape,
  })
  .superRefine((values, ctx) => {
    if (!values.partlyUsed) return;
    const remaining = values.remainingWeightGrams.trim();
    const issue = (message: string) =>
      ctx.addIssue({
        code: 'custom',
        path: ['remainingWeightGrams'],
        message,
      });
    if (remaining === '') issue('Enter the remaining weight');
    else if (!(Number(remaining) > 0)) issue('Must be greater than 0');
    else if (Number(remaining) > Number(values.spoolWeightGrams))
      issue('Exceeds spool weight');
  });

type FormValues = z.infer<typeof schema>;

/** Today as yyyy-mm-dd in the operator's own time zone, for a date input. */
function todayAsDateInput() {
  const now = new Date();
  const pad = (value: number) => String(value).padStart(2, '0');
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

function emptyForm(): FormValues {
  return {
    filamentVariantId: '',
    quantity: '1',
    spoolWeightGrams: '1000',
    label: '',
    partlyUsed: false,
    remainingWeightGrams: '',
    ...NO_PURCHASE,
    receivedOn: todayAsDateInput(),
  };
}

/** Register in Action required opens this page with what the printer reads: the tag, the likely color, its weights. */
function rfidPrefill(params: URLSearchParams): Partial<FormValues> {
  const weight = params.get('spoolWeightGrams');
  const remaining = params.get('remainingWeightGrams');
  return {
    ...(params.get('variantId')
      ? { filamentVariantId: params.get('variantId') ?? '' }
      : {}),
    ...(weight ? { spoolWeightGrams: weight } : {}),
    ...(weight && remaining && Number(remaining) < Number(weight)
      ? { partlyUsed: true, remainingWeightGrams: remaining }
      : {}),
  };
}

export function SpoolFormPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const filaments = useFilamentCatalog();
  const [searchParams] = useSearchParams();
  const rfidId = searchParams.get('rfidId');
  const rfidSlot = searchParams.get('slot');

  const options: FilamentOption[] = filaments.flatMap((filament) =>
    (filament.variants ?? []).map((variant) => ({
      id: variant.id ?? '',
      vendor: filament.vendorName ?? '',
      material: filament.material ?? '',
      colorName: variant.colorName ?? '',
      colorHex: variant.colorHex ?? '',
    })),
  );

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { ...emptyForm(), ...rfidPrefill(searchParams) },
  });

  const { ConfirmLeaveDialog, markClean } = useUnsavedChangesPrompt(
    form.formState.isDirty,
  );

  const [weightText, quantityText, partlyUsed, remainingText, priceText] =
    form.watch([
      'spoolWeightGrams',
      'quantity',
      'partlyUsed',
      'remainingWeightGrams',
      'price',
    ]);
  const weight = Number(weightText) > 0 ? Number(weightText) : 0;
  const quantityValue = quantityText.trim() === '' ? 1 : Number(quantityText);
  const quantity =
    Number.isInteger(quantityValue) &&
    quantityValue >= 1 &&
    quantityValue <= MAX_QUANTITY
      ? quantityValue
      : null;
  const remaining =
    partlyUsed && Number(remainingText) > 0
      ? Math.min(Number(remainingText), weight || Number(remainingText))
      : weight;
  const fraction = weight > 0 ? Math.min(1, remaining / weight) : 1;
  const price =
    priceText.trim() !== '' && Number(priceText) >= 0
      ? Number(priceText)
      : null;

  const each = quantity !== null && quantity > 1 ? ' each' : '';
  const weightLine = [
    quantity !== null ? pluralize(quantity, 'spool') : null,
    weight > 0
      ? partlyUsed && remaining < weight
        ? `${formatWeight(remaining)} left of ${formatWeight(weight)}${each}`
        : `${formatWeight(weight)}${each}`
      : null,
    !partlyUsed && quantity !== null && quantity > 1 && weight > 0
      ? `${formatWeight(quantity * weight)} total`
      : null,
  ]
    .filter(Boolean)
    .join(' · ');
  const priceLine =
    price !== null && quantity !== null
      ? `${formatCost(price)} per spool${quantity > 1 ? ` · ${formatCost(price * quantity)} total` : ''}`
      : null;

  const registerMutation = useOpenFarmApiEndpointsSpoolsRegisterSpoolEndpoint({
    mutation: {
      onSuccess: (_response, variables) => {
        const added = variables.data.quantity ?? 1;
        toast.success(
          added === 1 ? 'Spool added.' : `${pluralize(added, 'spool')} added.`,
        );
        queryClient.invalidateQueries({
          queryKey: getOpenFarmApiEndpointsSpoolsListSpoolsEndpointQueryKey(),
        });
      },
      onError: async (error) => {
        toast.error(await extractErrorDetail(error, 'Could not add spool.'));
      },
    },
  });

  /** Saves the spools, then leaves, or (for a delivery with several colors) starts the next one. */
  function submit(values: FormValues, addAnother: boolean) {
    registerMutation.mutate(
      {
        data: {
          filamentVariantId: values.filamentVariantId,
          label: values.label.trim() || null,
          spoolWeightGrams: Number(values.spoolWeightGrams),
          remainingWeightGrams: values.partlyUsed
            ? Number(values.remainingWeightGrams)
            : null,
          quantity: rfidId
            ? 1
            : values.quantity.trim() === ''
              ? 1
              : Number(values.quantity),
          rfidId,
          ...purchaseRequest(values),
        },
      },
      {
        onSuccess: () => {
          if (!addAnother) {
            markClean();
            navigate('/spools');
            return;
          }
          // What a delivery shares carries over; what differs per color starts again.
          form.reset({
            ...emptyForm(),
            spoolWeightGrams: values.spoolWeightGrams,
            supplier: values.supplier,
            orderNumber: values.orderNumber,
            receivedOn: values.receivedOn,
          });
          // reset() empties the form's field registry until the next render, and setFocus looks the field up at once.
          setTimeout(() => form.setFocus('filamentVariantId'), 0);
        },
      },
    );
  }

  return (
    <Page className="flex-1">
      <PageHeader
        above={
          <BackLink to="/spools" className="mb-1">
            Back to spools
          </BackLink>
        }
        title="Add spool"
        description={
          rfidId && rfidSlot
            ? `Loads into ${rfidSlot} automatically.`
            : undefined
        }
      />

      <Form {...form}>
        <form
          onSubmit={form.handleSubmit((values) => submit(values, false))}
          className="flex flex-1 flex-col gap-6"
        >
          <FieldGroup className="flex-1 gap-6">
            <Card className="max-w-3xl gap-0 py-0">
              <FormField
                control={form.control}
                name="filamentVariantId"
                render={({ field }) => (
                  <FormItem className="gap-0 data-[invalid=true]:bg-destructive/5">
                    <FormLabel className="sr-only">Filament</FormLabel>
                    <FormControl>
                      {/* The first decision is the filament, so the page opens on it. autoFocus, not setFocus: the popover swaps the trigger's node right after mount, which would strand a deferred focus on the old one. */}
                      <SpoolLabel
                        autoFocus
                        ref={field.ref}
                        onBlur={field.onBlur}
                        options={options}
                        value={field.value}
                        onValueChange={field.onChange}
                        fraction={fraction}
                      >
                        {weightLine && <p>{weightLine}</p>}
                        {priceLine && <p>{priceLine}</p>}
                      </SpoolLabel>
                    </FormControl>
                    <FormMessage className="px-5 pb-3" />
                  </FormItem>
                )}
              />
              <Separator />

              <div className="flex flex-col gap-5 p-5">
                <div
                  className={
                    rfidId
                      ? 'grid grid-cols-2 items-start gap-4 sm:grid-cols-[9rem_minmax(0,1fr)]'
                      : 'grid grid-cols-2 items-start gap-4 sm:grid-cols-[9rem_9rem_minmax(0,1fr)]'
                  }
                >
                  {/* One tag is one spool. */}
                  {!rfidId && (
                    <FormField
                      control={form.control}
                      name="quantity"
                      render={({ field }) => (
                        <FormItem>
                          <FormLabel>Quantity</FormLabel>
                          <FormControl>
                            <QuantityStepper
                              ref={field.ref}
                              name={field.name}
                              onBlur={field.onBlur}
                              value={field.value}
                              onValueChange={field.onChange}
                              min={1}
                              max={MAX_QUANTITY}
                            />
                          </FormControl>
                          <FormMessage />
                        </FormItem>
                      )}
                    />
                  )}
                  <FormField
                    control={form.control}
                    name="spoolWeightGrams"
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel>Spool weight</FormLabel>
                        <FormControl>
                          <UnitInput unit="g" {...field} />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                  <FormField
                    control={form.control}
                    name="label"
                    render={({ field }) => (
                      <FormItem className="col-span-2 sm:col-span-1">
                        <FormLabel>Label</FormLabel>
                        <FormControl>
                          <Input placeholder="Shelf B, spool 3" {...field} />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                </div>

                <div className="flex flex-wrap items-start gap-x-6 gap-y-3">
                  <FormField
                    control={form.control}
                    name="partlyUsed"
                    render={({ field }) => (
                      <FormItem
                        orientation="horizontal"
                        className="h-8 w-auto items-center"
                      >
                        <FormControl>
                          <Switch
                            checked={field.value}
                            onCheckedChange={field.onChange}
                            onBlur={field.onBlur}
                            name={field.name}
                            ref={field.ref}
                          />
                        </FormControl>
                        <FormLabel className="font-normal">
                          Partly used
                        </FormLabel>
                      </FormItem>
                    )}
                  />
                  {partlyUsed && (
                    <FormField
                      control={form.control}
                      name="remainingWeightGrams"
                      render={({ field }) => (
                        <FormItem className="w-44">
                          <FormLabel className="sr-only">
                            Remaining weight
                          </FormLabel>
                          <FormControl>
                            <UnitInput unit="g left" {...field} />
                          </FormControl>
                          <FormMessage />
                        </FormItem>
                      )}
                    />
                  )}
                </div>

                <Separator />

                <FieldSet className="gap-3">
                  <FieldLegend
                    variant="label"
                    className="mb-3 flex items-baseline gap-2"
                  >
                    Purchase
                    <span className="text-xs font-normal text-muted-foreground">
                      Optional
                    </span>
                  </FieldLegend>
                  <PurchaseFields spoolWeightGrams={weight} />
                </FieldSet>
              </div>
            </Card>

            <FormFooter
              end={
                <>
                  <Button
                    type="button"
                    variant="outline"
                    onClick={() => navigate('/spools')}
                  >
                    Cancel
                  </Button>
                  {!rfidId && (
                    <Button
                      type="button"
                      variant="outline"
                      disabled={registerMutation.isPending}
                      onClick={form.handleSubmit((values) =>
                        submit(values, true),
                      )}
                    >
                      Save and add another
                    </Button>
                  )}
                  <Button type="submit" disabled={registerMutation.isPending}>
                    {registerMutation.isPending && (
                      <Spinner data-icon="inline-start" />
                    )}
                    {registerMutation.isPending ? 'Saving…' : 'Save'}
                  </Button>
                </>
              }
            />
          </FieldGroup>
        </form>
      </Form>

      <ConfirmLeaveDialog />
    </Page>
  );
}
