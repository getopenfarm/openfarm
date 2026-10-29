import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { ChevronDownIcon, PlusIcon, XIcon } from 'lucide-react';
import { useRef, useState } from 'react';
import { useFieldArray, useForm, useWatch } from 'react-hook-form';
import { Link, useNavigate, useParams } from 'react-router';
import { toast } from 'sonner';
import { z } from 'zod';
import { BackLink } from '@/components/back-link/BackLink';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { FormFooter } from '@/components/form-footer/FormFooter';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import {
  Card,
  CardContent,
  CardFooter,
  CardHeader,
  CardTitle,
} from '@/components/ui/card';
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from '@/components/ui/collapsible';
import {
  Field,
  FieldDescription,
  FieldError,
  FieldGroup,
  FieldLabel,
  FieldLegend,
  FieldSet,
} from '@/components/ui/field';
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from '@/components/ui/form';
import { Input } from '@/components/ui/input';
import {
  InputGroup,
  InputGroupAddon,
  InputGroupInput,
} from '@/components/ui/input-group';
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Skeleton } from '@/components/ui/skeleton';
import { Spinner } from '@/components/ui/spinner';
import { feeTotals } from '@/features/assemblies/pricing';
import { useUnsavedChangesPrompt } from '@/hooks/useUnsavedChangesPrompt';
import type {
  FeeProfileDto,
  FeesResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { httpClient } from '@/lib/api/httpClient';
import { formatCost } from '@/lib/formatUnits';
import { useFeeProfileRemoval } from './FeeProfileRemoval';
import { feeSummary, feesKey, useFeeProfiles } from './fees';
import { useCurrency } from './generalSettings';

const number = z
  .string()
  .refine(
    (v) => v.trim() !== '' && Number.isFinite(Number(v)) && Number(v) >= 0,
    'Enter a finite number of 0 or more.',
  );
const schema = z
  .object({
    name: z
      .string()
      .trim()
      .min(1, 'Name required.')
      .max(100, 'Enter 100 characters or fewer.'),
    fees: z.array(
      z
        .object({
          name: z.string().trim().min(1, 'Name required.'),
          type: z.enum(['Percent', 'Fixed']),
          amount: number,
        })
        .superRefine((fee, ctx) => {
          if (fee.type === 'Percent' && Number(fee.amount) >= 100)
            ctx.addIssue({
              code: 'custom',
              path: ['amount'],
              message: 'Enter below 100%.',
            });
        }),
    ),
  })
  .superRefine((profile, ctx) => {
    if (
      profile.fees
        .filter((f) => f.type === 'Percent')
        .reduce((sum, f) => sum + (Number(f.amount) || 0), 0) >= 100
    )
      ctx.addIssue({
        code: 'custom',
        path: ['fees'],
        message: 'Percent fees must total below 100%.',
      });
  });
type Values = z.infer<typeof schema>;
const valuesFor = (profile?: FeeProfileDto): Values => ({
  name: profile?.name ?? '',
  fees: (profile?.fees ?? []).map((fee) => ({
    name: fee.name ?? '',
    type: fee.type === 'Fixed' ? 'Fixed' : 'Percent',
    amount: String(fee.amount ?? 0),
  })),
});

export function FeeProfilePage() {
  const { id } = useParams();
  const query = useFeeProfiles();
  const profile = query.data?.data.profiles?.find((p) => p.id === id);
  if (query.data && (!id || profile))
    return <FeeProfileEditor key={id ?? 'new'} profile={profile} />;
  return (
    <Page>
      <PageHeader
        title={id ? 'Fee profile' : 'Add fee profile'}
        size="section"
        above={<BackLink to="/settings/fees">Back to Fees</BackLink>}
      />
      {query.isError ? (
        <ErrorState
          title="Could not load fee profile"
          onRetry={() => void query.refetch()}
        />
      ) : query.data ? (
        <EmptyState
          title="Fee profile not found"
          action={
            <Button asChild>
              <Link to="/settings/fees">Back to Fees</Link>
            </Button>
          }
        />
      ) : (
        <Skeleton className="h-40" />
      )}
    </Page>
  );
}

function FeeProfileEditor({ profile }: { profile?: FeeProfileDto }) {
  const client = useQueryClient();
  const navigate = useNavigate();
  const currency = useCurrency();
  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: valuesFor(profile),
  });
  const fees = useFieldArray({ control: form.control, name: 'fees' });
  const watched = useWatch({ control: form.control, name: 'fees' });
  const { ConfirmLeaveDialog, markClean } = useUnsavedChangesPrompt(
    form.formState.isDirty,
  );
  const removal = useFeeProfileRemoval(() => {
    markClean();
    void navigate('/settings/fees');
  });
  const save = useMutation({
    mutationFn: async (values: Values) => {
      const result = await httpClient<{ data: FeeProfileDto }>(
        `${feesKey}/profiles${profile ? `/${profile.id}` : ''}`,
        {
          method: profile ? 'PUT' : 'POST',
          body: JSON.stringify({
            name: values.name.trim(),
            fees: values.fees.map((f) => ({
              name: f.name.trim(),
              type: f.type,
              amount: Number(f.amount),
            })),
          }),
        },
      );
      return result;
    },
    onSuccess: (result) => {
      client.setQueryData<{ data: FeesResponse }>([feesKey], (previous) =>
        previous
          ? {
              ...previous,
              data: {
                profiles: [
                  ...(previous.data.profiles ?? []).filter(
                    (p) => p.id !== result.data.id,
                  ),
                  result.data,
                ].sort((a, b) => (a.name ?? '').localeCompare(b.name ?? '')),
              },
            }
          : previous,
      );
      void client.invalidateQueries({ queryKey: [feesKey] });
      form.reset(valuesFor(result.data));
      markClean();
      toast.success(profile ? 'Fee profile saved.' : 'Fee profile added.');
      void navigate('/settings/fees');
    },
    onError: async (error) => {
      if (error instanceof Response && error.status === 400) {
        const body = await error
          .clone()
          .json()
          .catch(() => null);
        const messages: string[] = Object.values(
          body?.errors ?? {},
        ).flat() as string[];
        if (messages.some((message) => message.includes('unique')))
          form.setError(
            'name',
            { message: 'Fee profile names must be unique.' },
            { shouldFocus: true },
          );
      }
    },
  });
  const pending = save.isPending || removal.pending;
  const submit = form.handleSubmit((values) => {
    if (pending) return;
    const existing = client.getQueryData<{ data: FeesResponse }>([feesKey]);
    if (
      existing?.data.profiles?.some(
        (p) =>
          p.id !== profile?.id &&
          p.name?.trim().toUpperCase() === values.name.trim().toUpperCase(),
      )
    ) {
      form.setError(
        'name',
        { message: 'Fee profile names must be unique.' },
        { shouldFocus: true },
      );
      return;
    }
    save.mutate(values);
  });
  const totalsValid = watched.every(
    (f) =>
      Number.isFinite(Number(f.amount)) &&
      f.amount.trim() !== '' &&
      Number(f.amount) >= 0 &&
      (f.type !== 'Percent' || Number(f.amount) < 100),
  );
  const total = feeTotals(
    watched.map((f) => ({ type: f.type, amount: Number(f.amount) || 0 })),
  );
  const valid =
    totalsValid && total.feePercent < 100 && Number.isFinite(total.fixedFee);
  const totalError =
    form.formState.errors.fees?.root?.message ??
    form.formState.errors.fees?.message;
  const feeNameRefs = useRef(new Map<string, HTMLInputElement>());
  const addRef = useRef<HTMLButtonElement>(null);
  function removeFee(index: number) {
    const nextId = fees.fields[index + 1]?.id ?? fees.fields[index - 1]?.id;
    fees.remove(index);
    if (nextId) feeNameRefs.current.get(nextId)?.focus();
    else addRef.current?.focus();
  }
  return (
    <Page className="flex-1">
      <PageHeader
        title={profile?.name ?? 'Add fee profile'}
        size="section"
        above={<BackLink to="/settings/fees">Back to Fees</BackLink>}
        action={
          profile ? (
            <Button
              type="button"
              variant="destructive"
              disabled={pending}
              onClick={(event) => {
                removal.returnFocusRef.current = event.currentTarget;
                removal.open(profile);
              }}
            >
              Remove
            </Button>
          ) : undefined
        }
      />
      <Form {...form}>
        <form className="flex flex-1 flex-col gap-4" onSubmit={submit}>
          <FieldGroup className="max-w-[800px] flex-1">
            <Card>
              <CardHeader>
                <CardTitle className="sr-only">Fee profile</CardTitle>
                <FormField
                  control={form.control}
                  name="name"
                  render={({ field }) => (
                    <FormItem data-disabled={pending || undefined}>
                      <FormLabel>Profile name</FormLabel>
                      <FormControl>
                        <Input
                          {...field}
                          disabled={pending}
                          placeholder="e.g. Shopee or Direct"
                          className="sm:max-w-80"
                        />
                      </FormControl>
                      <FormMessage />
                    </FormItem>
                  )}
                />
              </CardHeader>
              <CardContent className="@container/fee-editor">
                <FieldSet className="gap-3">
                  <FieldLegend variant="label">Fees</FieldLegend>
                  <FieldGroup className="gap-3 *:data-[slot=field-group]:gap-3">
                    {fees.fields.length === 0 ? (
                      <FieldDescription>
                        No fees added. This profile has no sale fees.
                      </FieldDescription>
                    ) : (
                      <>
                        <div
                          aria-hidden="true"
                          className="hidden grid-cols-[minmax(0,1fr)_11rem_8rem_auto] items-start gap-3 text-sm text-muted-foreground @min-[560px]/fee-editor:grid"
                        >
                          <span>Fee name</span>
                          <span>Type</span>
                          <span>Amount</span>
                          <span className="size-7" />
                        </div>
                        {fees.fields.map((row, index) => (
                          <FieldGroup
                            key={row.id}
                            className="grid grid-cols-[minmax(0,1fr)_minmax(0,1fr)_auto] items-start gap-3 @min-[560px]/fee-editor:grid-cols-[minmax(0,1fr)_11rem_8rem_auto]"
                          >
                            <FormField
                              control={form.control}
                              name={`fees.${index}.name`}
                              render={({ field }) => (
                                <FormItem
                                  className="min-w-0 col-span-3 @min-[560px]/fee-editor:col-span-1"
                                  data-disabled={pending || undefined}
                                >
                                  <FormLabel className="@min-[560px]/fee-editor:sr-only">
                                    Fee name
                                  </FormLabel>
                                  <FormControl>
                                    <Input
                                      {...field}
                                      ref={(node) => {
                                        field.ref(node);
                                        if (node)
                                          feeNameRefs.current.set(row.id, node);
                                        else feeNameRefs.current.delete(row.id);
                                      }}
                                      disabled={pending}
                                      placeholder="e.g. Commission"
                                    />
                                  </FormControl>
                                  <FormMessage />
                                </FormItem>
                              )}
                            />
                            <FormField
                              control={form.control}
                              name={`fees.${index}.type`}
                              render={({ field }) => (
                                <FormItem
                                  className="min-w-0"
                                  data-disabled={pending || undefined}
                                >
                                  <FormLabel className="@min-[560px]/fee-editor:sr-only">
                                    Type
                                  </FormLabel>
                                  <Select
                                    value={field.value}
                                    onValueChange={(value) => {
                                      field.onChange(value);
                                      void form.trigger('fees');
                                    }}
                                    disabled={pending}
                                  >
                                    <FormControl>
                                      <SelectTrigger className="w-full">
                                        <SelectValue>
                                          {field.value === 'Percent'
                                            ? 'Percentage'
                                            : 'Fixed per order'}
                                        </SelectValue>
                                      </SelectTrigger>
                                    </FormControl>
                                    <SelectContent>
                                      <SelectGroup>
                                        <SelectItem value="Percent">
                                          Percentage of sale price
                                        </SelectItem>
                                        <SelectItem value="Fixed">
                                          Fixed per order
                                        </SelectItem>
                                      </SelectGroup>
                                    </SelectContent>
                                  </Select>
                                  <FormMessage />
                                </FormItem>
                              )}
                            />
                            <FormField
                              control={form.control}
                              name={`fees.${index}.amount`}
                              render={({ field }) => (
                                <FormItem
                                  className="min-w-0"
                                  data-disabled={pending || undefined}
                                >
                                  <FormLabel className="@min-[560px]/fee-editor:sr-only">
                                    Amount
                                  </FormLabel>
                                  <InputGroup>
                                    <FormControl>
                                      <InputGroupInput
                                        {...field}
                                        inputMode="decimal"
                                        disabled={pending}
                                      />
                                    </FormControl>
                                    <InputGroupAddon align="inline-end">
                                      {watched[index]?.type === 'Fixed'
                                        ? currency
                                        : '%'}
                                    </InputGroupAddon>
                                  </InputGroup>
                                  <FormMessage />
                                </FormItem>
                              )}
                            />
                            <Button
                              type="button"
                              variant="ghost"
                              size="icon-sm"
                              className="mt-7 @min-[560px]/fee-editor:mt-0.5"
                              aria-label={`Remove ${watched[index]?.name || `fee ${index + 1}`}`}
                              disabled={pending}
                              onClick={() => removeFee(index)}
                            >
                              <XIcon />
                            </Button>
                          </FieldGroup>
                        ))}
                      </>
                    )}
                    <Button
                      ref={addRef}
                      type="button"
                      variant="outline"
                      size="sm"
                      className="self-start"
                      disabled={pending}
                      onClick={() =>
                        fees.append(
                          { name: '', type: 'Percent', amount: '' },
                          { focusName: `fees.${fees.fields.length}.name` },
                        )
                      }
                    >
                      <PlusIcon data-icon="inline-start" />
                      Add fee
                    </Button>
                  </FieldGroup>
                </FieldSet>
              </CardContent>
              <CardFooter className="flex-col items-stretch gap-2">
                <div
                  className="flex flex-wrap items-baseline justify-between gap-2"
                  aria-live="polite"
                >
                  <span className="text-sm font-medium">Combined fees</span>
                  <span className="text-sm tabular-nums">
                    {valid
                      ? feeSummary(total.feePercent, total.fixedFee, currency)
                      : 'Enter valid fee amounts to see the total.'}
                  </span>
                </div>
                <FieldError>{totalError}</FieldError>
              </CardFooter>
            </Card>
            <FieldGroup className="gap-1">
              <SaleExample
                percent={total.feePercent}
                fixed={total.fixedFee}
                valid={valid}
                currency={currency}
                disabled={pending}
              />
              <Collapsible>
                <CollapsibleTrigger asChild>
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    className="h-auto max-w-full justify-start whitespace-normal text-left"
                    disabled={pending}
                  >
                    <ChevronDownIcon
                      data-icon="inline-start"
                      className="-rotate-90 transition-transform [[data-state=open]>&]:rotate-0"
                    />
                    Include a monthly fee
                  </Button>
                </CollapsibleTrigger>
                <CollapsibleContent className="px-2.5 pt-2 pb-3">
                  <FieldDescription>
                    Divide the monthly fee by your expected orders per month,
                    then add it as a fixed fee per order. For example, 48 ÷ 400
                    = 0.12 per order.
                  </FieldDescription>
                </CollapsibleContent>
              </Collapsible>
            </FieldGroup>
            {save.isError && (
              <ErrorState
                title="Could not save fee profile"
                description="Your changes are still here. Check the fields and try again."
                onRetry={() => void submit()}
              />
            )}
          </FieldGroup>
          <FormFooter
            className="-mx-7 -mb-12 px-7"
            end={
              <>
                <Button
                  type="button"
                  variant="outline"
                  disabled={pending}
                  onClick={() => void navigate('/settings/fees')}
                >
                  Cancel
                </Button>
                <Button
                  type="submit"
                  disabled={pending || !form.formState.isDirty}
                >
                  {save.isPending && (
                    <Spinner data-icon="inline-start" aria-hidden="true" />
                  )}
                  {save.isPending ? 'Saving…' : 'Save'}
                </Button>
              </>
            }
          />
        </form>
      </Form>
      {removal.dialog}
      <ConfirmLeaveDialog />
    </Page>
  );
}

function SaleExample({
  percent,
  fixed,
  valid,
  currency,
  disabled,
}: {
  percent: number;
  fixed: number;
  valid: boolean;
  currency: string;
  disabled: boolean;
}) {
  const [sale, setSale] = useState('100');
  const saleValid =
    sale.trim() !== '' && Number.isFinite(Number(sale)) && Number(sale) >= 0;
  const fee =
    valid && saleValid ? (Number(sale) * percent) / 100 + fixed : null;
  return (
    <Collapsible>
      <CollapsibleTrigger asChild>
        <Button
          type="button"
          variant="ghost"
          size="sm"
          className="h-auto max-w-full justify-start whitespace-normal text-left"
          disabled={disabled}
        >
          <ChevronDownIcon
            data-icon="inline-start"
            className="-rotate-90 transition-transform [[data-state=open]>&]:rotate-0"
          />
          Try a sale amount
        </Button>
      </CollapsibleTrigger>
      <CollapsibleContent className="px-2.5 pt-2 pb-3">
        <FieldGroup className="gap-3">
          <FieldDescription>
            Check how these fees affect a sale. Production costs are excluded.
          </FieldDescription>
          <Field
            data-invalid={!saleValid || undefined}
            data-disabled={disabled || undefined}
            className="max-w-48"
          >
            <FieldLabel htmlFor="fee-example-sale">Sale amount</FieldLabel>
            <InputGroup>
              <InputGroupInput
                id="fee-example-sale"
                value={sale}
                onChange={(event) => setSale(event.target.value)}
                inputMode="decimal"
                aria-invalid={!saleValid}
                aria-describedby={!saleValid ? 'fee-example-error' : undefined}
                disabled={disabled}
              />
              <InputGroupAddon align="inline-end">{currency}</InputGroupAddon>
            </InputGroup>
            {!saleValid && (
              <FieldError id="fee-example-error">
                Enter a finite number of 0 or more.
              </FieldError>
            )}
          </Field>
          <dl
            className="grid grid-cols-1 gap-3 text-sm sm:grid-cols-2"
            aria-live="polite"
          >
            <div>
              <dt className="text-muted-foreground">Total fees</dt>
              <dd className="tabular-nums">
                {fee === null ? '—' : formatCost(fee, currency)}
              </dd>
            </div>
            <div>
              <dt className="text-muted-foreground">Remaining after fees</dt>
              <dd className="tabular-nums">
                {fee === null ? '—' : formatCost(Number(sale) - fee, currency)}
              </dd>
            </div>
          </dl>
        </FieldGroup>
      </CollapsibleContent>
    </Collapsible>
  );
}
