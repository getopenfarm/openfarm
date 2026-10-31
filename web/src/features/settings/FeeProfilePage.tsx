import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { PlusIcon, XIcon } from 'lucide-react';
import { useRef } from 'react';
import { useFieldArray, useForm, useWatch } from 'react-hook-form';
import { Link, useNavigate, useParams } from 'react-router';
import { toast } from 'sonner';
import { z } from 'zod';
import { BackLink } from '@/components/back-link/BackLink';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { PermissionButton as Button } from '@/components/PermissionButton';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import {
  Card,
  CardContent,
  CardFooter,
  CardHeader,
  CardTitle,
} from '@/components/ui/card';
import {
  FieldDescription,
  FieldError,
  FieldGroup,
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
    mode: 'onSubmit',
    reValidateMode: 'onSubmit',
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
    <Page>
      <PageHeader
        title={profile?.name ?? 'Add fee profile'}
        size="section"
        above={<BackLink to="/settings/fees">Back to Fees</BackLink>}
        action={
          profile ? (
            <Button
              permission="settings:fees:update"
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
        <form className="flex max-w-[800px] flex-col gap-4" onSubmit={submit}>
          <FieldGroup>
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
                                    onValueChange={field.onChange}
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
                      permission="settings:fees:update"
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
                      : '—'}
                  </span>
                </div>
                <FieldError>{totalError}</FieldError>
              </CardFooter>
            </Card>
            {save.isError && (
              <ErrorState
                title="Could not save fee profile"
                description="Your changes are still here. Check the fields and try again."
                onRetry={() => void submit()}
              />
            )}
          </FieldGroup>
          <div className="flex flex-wrap items-center gap-2">
            <Button
              permission="settings:fees:update"
              type="submit"
              disabled={pending || !form.formState.isDirty}
            >
              {save.isPending && (
                <Spinner data-icon="inline-start" aria-hidden="true" />
              )}
              {save.isPending ? 'Saving…' : 'Save'}
            </Button>
            <Button
              type="button"
              variant="outline"
              disabled={pending}
              onClick={() => void navigate('/settings/fees')}
            >
              Cancel
            </Button>
          </div>
        </form>
      </Form>
      {removal.dialog}
      <ConfirmLeaveDialog />
    </Page>
  );
}
