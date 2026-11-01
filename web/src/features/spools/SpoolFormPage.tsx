import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { PlusIcon, Trash2Icon } from 'lucide-react';
import { useFieldArray, useForm, useFormContext } from 'react-hook-form';
import { useNavigate, useSearchParams } from 'react-router';
import { toast } from 'sonner';
import { z } from 'zod';
import { BackLink } from '@/components/back-link/BackLink';
import { FormFooter } from '@/components/form-footer/FormFooter';
import { PermissionButton as Button } from '@/components/PermissionButton';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
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
import { MoneyInput } from '@/components/ui/money-input';
import { Separator } from '@/components/ui/separator';
import { Spinner } from '@/components/ui/spinner';
import { useFilamentCatalog } from '@/features/filament/data/useFilamentCatalog';
import { useCurrency } from '@/features/settings/generalSettings';
import { useUnsavedChangesPrompt } from '@/hooks/useUnsavedChangesPrompt';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import type { RegisterSpoolsResponse } from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsSpoolsRegisterSpoolEndpoint } from '@/lib/api/generated/spools/spools';
import { formatCost, formatWeight } from '@/lib/formatUnits';
import { invalidateFilamentTrackingQueries } from '@/lib/invalidateFilamentTrackingQueries';
import { pluralize } from '@/lib/pluralize';
import {
  PurchaseFields,
  sharedPurchaseRequest,
  sharedPurchaseShape,
} from './components/PurchaseFields';
import { QuantityStepper } from './components/QuantityStepper';
import { type FilamentOption, SpoolLabel } from './components/SpoolLabel';
import { UnitInput } from './components/UnitInput';

// The server's limits for one delivery.
const MAX_LINES = 20;
const MAX_QUANTITY = 500;
const MAX_SPOOLS = 1000;

/** A line's spool count: blank is one; anything but a whole number from 1 to MAX_QUANTITY is null. */
function quantityOf(text: string) {
  const value = text.trim() === '' ? 1 : Number(text);
  return Number.isInteger(value) && value >= 1 && value <= MAX_QUANTITY
    ? value
    : null;
}

const lineSchema = z
  .object({
    filamentVariantId: z.string().min(1, 'Choose a color'),
    quantity: z
      .string()
      .refine(
        (value) => quantityOf(value) !== null,
        `Enter a whole number from 1 to ${MAX_QUANTITY}`,
      ),
    spoolWeightGrams: z
      .string()
      .refine((value) => Number(value) > 0, 'Must be greater than 0'),
    // Blank is a full spool.
    remainingWeightGrams: z.string(),
    price: z
      .string()
      .refine(
        (value) => value.trim() === '' || Number(value) >= 0,
        'Must be 0 or more',
      ),
  })
  .superRefine((line, ctx) => {
    const left = line.remainingWeightGrams.trim();
    if (left === '') return;
    const issue = (message: string) =>
      ctx.addIssue({ code: 'custom', path: ['remainingWeightGrams'], message });
    if (!(Number(left) > 0)) issue('Must be greater than 0');
    else if (Number(left) > Number(line.spoolWeightGrams))
      issue('Exceeds spool weight');
  });

type LineValues = z.infer<typeof lineSchema>;

function totalSpools(lines: LineValues[]) {
  return lines.reduce((sum, line) => sum + (quantityOf(line.quantity) ?? 0), 0);
}

const schema = z
  .object({
    lines: z.array(lineSchema).min(1).max(MAX_LINES),
    ...sharedPurchaseShape,
  })
  .superRefine((values, ctx) => {
    if (totalSpools(values.lines) > MAX_SPOOLS)
      ctx.addIssue({
        code: 'custom',
        path: ['lines'],
        message: `Up to ${MAX_SPOOLS} spools per delivery`,
      });
  });

type FormValues = z.infer<typeof schema>;

/** The grams each spool of a line holds: what is left when given, else its full weight. */
function gramsEach(line: LineValues) {
  const left = Number(line.remainingWeightGrams);
  if (line.remainingWeightGrams.trim() !== '' && left > 0) return left;
  return Number(line.spoolWeightGrams) > 0 ? Number(line.spoolWeightGrams) : 0;
}

/** Today as yyyy-mm-dd in the operator's own time zone, for a date input. */
function todayAsDateInput() {
  const now = new Date();
  const pad = (value: number) => String(value).padStart(2, '0');
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

function newLine(from?: LineValues): LineValues {
  return {
    filamentVariantId: '',
    quantity: '1',
    // A delivery's rolls are usually one size and one price.
    spoolWeightGrams: from?.spoolWeightGrams ?? '1000',
    remainingWeightGrams: '',
    price: from?.price ?? '',
  };
}

/**
 * The first line, from the URL: a filament's page passes a color; Register in Action required passes what the printer reads
 * (the tag's likely color and weights).
 */
function firstLine(params: URLSearchParams): LineValues {
  const weight = params.get('spoolWeightGrams');
  const remaining = params.get('remainingWeightGrams');
  return {
    ...newLine(),
    filamentVariantId: params.get('variantId') ?? '',
    ...(weight ? { spoolWeightGrams: weight } : {}),
    ...(weight && remaining && Number(remaining) < Number(weight)
      ? { remainingWeightGrams: remaining }
      : {}),
  };
}

export function SpoolFormPage() {
  const currency = useCurrency();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const filaments = useFilamentCatalog();
  const [searchParams] = useSearchParams();
  const rfidId = searchParams.get('rfidId');
  const rfidSlot = searchParams.get('slot');
  // Opened from a filament's page: only its colors, and back to its spools.
  const filamentId = searchParams.get('filamentId');
  const spoolsUrl = filamentId
    ? `/filament/${filamentId}?tab=spools`
    : '/filament';

  const options: FilamentOption[] = filaments
    .filter((filament) => !filamentId || filament.id === filamentId)
    .flatMap((filament) =>
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
    defaultValues: {
      lines: [firstLine(searchParams)],
      supplier: '',
      orderNumber: '',
      receivedOn: todayAsDateInput(),
    },
  });
  const lines = useFieldArray({ control: form.control, name: 'lines' });

  const { ConfirmLeaveDialog, markClean } = useUnsavedChangesPrompt(
    form.formState.isDirty,
  );

  const watched = form.watch('lines');
  const total = totalSpools(watched);
  const grams = watched.reduce(
    (sum, line) => sum + (quantityOf(line.quantity) ?? 0) * gramsEach(line),
    0,
  );
  // A total cost only when every line has a price.
  const cost = watched.every(
    (line) => line.price.trim() !== '' && Number(line.price) >= 0,
  )
    ? watched.reduce(
        (sum, line) =>
          sum + (quantityOf(line.quantity) ?? 0) * Number(line.price),
        0,
      )
    : null;
  const summary =
    total > 0
      ? [
          pluralize(total, 'spool'),
          grams > 0 ? formatWeight(grams) : null,
          cost !== null ? formatCost(cost, currency) : null,
        ]
          .filter(Boolean)
          .join(' · ')
      : null;
  const linesError =
    form.formState.errors.lines?.root?.message ??
    form.formState.errors.lines?.message;

  const registerMutation = useOpenFarmApiEndpointsSpoolsRegisterSpoolEndpoint({
    mutation: {
      onSuccess: (response, variables) => {
        const added = (variables.data.lines ?? []).reduce(
          (sum, line) => sum + (line.quantity ?? 1),
          0,
        );
        toast.success(
          added === 1 ? 'Spool added.' : `${pluralize(added, 'spool')} added.`,
        );
        invalidateFilamentTrackingQueries(queryClient);
        markClean();
        // The server answers 201, which the generated type does not list.
        const saved = (response.data as RegisterSpoolsResponse).spools ?? [];
        const filamentIds = new Set(saved.map((spool) => spool.filamentId));
        const [only] = filamentIds;
        navigate(
          filamentIds.size === 1 && only
            ? `/filament/${only}?tab=spools`
            : '/filament',
        );
      },
      onError: async (error) => {
        toast.error(await extractErrorDetail(error, 'Could not add spools.'));
      },
    },
  });

  function submit(values: FormValues) {
    registerMutation.mutate({
      data: {
        lines: values.lines.map((line) => ({
          filamentVariantId: line.filamentVariantId,
          spoolWeightGrams: Number(line.spoolWeightGrams),
          quantity: quantityOf(line.quantity) ?? 1,
          remainingWeightGrams:
            line.remainingWeightGrams.trim() === ''
              ? null
              : Number(line.remainingWeightGrams),
          price: line.price.trim() === '' ? null : Number(line.price),
        })),
        rfidId,
        ...sharedPurchaseRequest(values),
      },
    });
  }

  return (
    <Page className="flex-1">
      <PageHeader
        above={
          <BackLink to={spoolsUrl} className="mb-1">
            {filamentId ? 'Back to spools' : 'Back to filament'}
          </BackLink>
        }
        title="Add spools"
        description={
          rfidId && rfidSlot
            ? `Loads into ${rfidSlot} automatically.`
            : undefined
        }
      />

      <Form {...form}>
        <form
          onSubmit={form.handleSubmit(submit)}
          className="flex flex-1 flex-col gap-6"
        >
          <FieldGroup className="flex-1 gap-6">
            {lines.fields.map((field, index) => (
              <SpoolLine
                key={field.id}
                index={index}
                options={options}
                // One tag is one spool.
                tagged={rfidId !== null}
                onRemove={
                  lines.fields.length > 1
                    ? () => lines.remove(index)
                    : undefined
                }
              />
            ))}

            {!rfidId && (
              <div className="flex max-w-3xl flex-col gap-2">
                <Button
                  permission="spools:register"
                  type="button"
                  variant="outline"
                  className="self-start"
                  disabled={lines.fields.length >= MAX_LINES}
                  onClick={() =>
                    lines.append(newLine(watched[watched.length - 1]))
                  }
                >
                  <PlusIcon data-icon="inline-start" />
                  Add line
                </Button>
                {linesError && (
                  <p role="alert" className="text-sm text-destructive">
                    {linesError}
                  </p>
                )}
              </div>
            )}

            <Card className="max-w-3xl gap-0 p-5">
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
                <PurchaseFields withPrice={false} />
              </FieldSet>
            </Card>

            <FormFooter
              end={
                <>
                  {summary && (
                    <span className="mr-2 text-sm text-muted-foreground tabular-nums">
                      {summary}
                    </span>
                  )}
                  <Button
                    type="button"
                    variant="outline"
                    onClick={() => navigate(spoolsUrl)}
                  >
                    Cancel
                  </Button>
                  <Button
                    permission="spools:override-weight"
                    type="submit"
                    disabled={registerMutation.isPending}
                  >
                    {registerMutation.isPending && (
                      <Spinner data-icon="inline-start" />
                    )}
                    {registerMutation.isPending
                      ? 'Adding…'
                      : total > 0
                        ? `Add ${pluralize(total, 'spool')}`
                        : 'Add spools'}
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

/** One line of the delivery: identical spools of one color, drawn as a spool label filled to what is left. */
function SpoolLine({
  index,
  options,
  tagged,
  onRemove,
}: {
  index: number;
  options: FilamentOption[];
  tagged: boolean;
  /** Absent while it is the only line. */
  onRemove?: () => void;
}) {
  const { control, watch } = useFormContext<FormValues>();
  const [weightText, leftText] = watch([
    `lines.${index}.spoolWeightGrams`,
    `lines.${index}.remainingWeightGrams`,
  ]);
  const weight = Number(weightText) > 0 ? Number(weightText) : 0;
  const left =
    leftText.trim() !== '' && Number(leftText) > 0
      ? Math.min(Number(leftText), weight || Number(leftText))
      : weight;
  const fraction = weight > 0 ? Math.min(1, left / weight) : 1;

  return (
    <Card
      role="group"
      aria-label={`Line ${index + 1}`}
      className="max-w-3xl gap-0 py-0"
    >
      <FormField
        control={control}
        name={`lines.${index}.filamentVariantId`}
        render={({ field }) => (
          <FormItem className="gap-0 data-[invalid=true]:bg-destructive/5">
            <FormLabel className="sr-only">Filament</FormLabel>
            <div className="flex items-center">
              <FormControl>
                {/* A line opens on its color, the first decision. autoFocus, not setFocus: the popover swaps the trigger's node right after mount, which would strand a deferred focus on the old one. */}
                <SpoolLabel
                  autoFocus
                  ref={field.ref}
                  onBlur={field.onBlur}
                  options={options}
                  value={field.value}
                  onValueChange={field.onChange}
                  fraction={fraction}
                  className="min-w-0 flex-1"
                />
              </FormControl>
              {onRemove && (
                <Button
                  type="button"
                  variant="ghost"
                  size="icon-sm"
                  aria-label={`Remove line ${index + 1}`}
                  className="mr-4"
                  onClick={onRemove}
                >
                  <Trash2Icon />
                </Button>
              )}
            </div>
            <FormMessage className="px-5 pb-3" />
          </FormItem>
        )}
      />
      <Separator />
      <div className="grid grid-cols-2 items-start gap-4 p-5 sm:grid-cols-4">
        {!tagged && (
          <FormField
            control={control}
            name={`lines.${index}.quantity`}
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
          control={control}
          name={`lines.${index}.spoolWeightGrams`}
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
          control={control}
          name={`lines.${index}.remainingWeightGrams`}
          render={({ field }) => (
            <FormItem>
              <FormLabel>Left</FormLabel>
              <FormControl>
                <UnitInput unit="g" placeholder="Full" {...field} />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />
        <FormField
          control={control}
          name={`lines.${index}.price`}
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
              <FormMessage />
            </FormItem>
          )}
        />
      </div>
    </Card>
  );
}
