import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { ChevronsUpDownIcon } from 'lucide-react';
import { useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { toast } from 'sonner';
import { z } from 'zod';
import { ErrorState } from '@/components/empty-state/EmptyState';
import { PermissionButton as Button } from '@/components/PermissionButton';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from '@/components/ui/command';
import {
  Field,
  FieldDescription,
  FieldError,
  FieldGroup,
  FieldLabel,
} from '@/components/ui/field';
import {
  InputGroup,
  InputGroupAddon,
  InputGroupInput,
} from '@/components/ui/input-group';
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from '@/components/ui/popover';
import { Skeleton } from '@/components/ui/skeleton';
import { Spinner } from '@/components/ui/spinner';
import { useUnsavedChangesPrompt } from '@/hooks/useUnsavedChangesPrompt';
import { httpClient } from '@/lib/api/httpClient';
import {
  type GeneralSettings,
  generalSettingsKey,
  useGeneralSettings,
} from './generalSettings';
import { invalidatePrinterCosts } from './printerCosts';

export function GeneralSettingsPage() {
  const query = useGeneralSettings();
  return (
    <Page>
      <PageHeader
        title="General"
        size="section"
        description="Shared pricing and electricity settings for your farm."
      />
      {query.data ? (
        <GeneralForm data={query.data.data} />
      ) : query.isError ? (
        <ErrorState
          title="Could not load general settings"
          onRetry={() => void query.refetch()}
        />
      ) : (
        <Skeleton className="h-40" />
      )}
    </Page>
  );
}

const tariff = z
  .string()
  .refine(
    (v) => v.trim() === '' || (Number.isFinite(Number(v)) && Number(v) >= 0),
    'Enter a nonnegative number.',
  );
const percent = z
  .string()
  .refine(
    (v) => v.trim() === '' || (Number(v) >= 0 && Number(v) < 100),
    'Enter 0 to 99.99.',
  );
const valuesFor = (data: GeneralSettings) => ({
  currencyCode: data.currencyCode,
  price:
    data.electricityPricePerKwh == null
      ? ''
      : String(data.electricityPricePerKwh),
  failure: String(data.failureAllowancePercent ?? 0),
});
function GeneralForm({ data }: { data: GeneralSettings }) {
  const client = useQueryClient();
  const [open, setOpen] = useState(false);
  const schema = z.object({
    currencyCode: z
      .string()
      .refine(
        (v) => data.supportedCurrencies.includes(v),
        'Select a supported currency.',
      ),
    price: tariff,
    failure: percent,
  });
  const form = useForm({
    resolver: zodResolver(schema),
    mode: 'onSubmit',
    reValidateMode: 'onSubmit',
    defaultValues: valuesFor(data),
  });
  const { ConfirmLeaveDialog } = useUnsavedChangesPrompt(
    form.formState.isDirty,
  );
  const currency = form.watch('currencyCode');
  const names = new Intl.DisplayNames(['en'], { type: 'currency' });
  const label = (code: string) => `${code} — ${names.of(code) ?? code}`;
  const save = useMutation({
    mutationFn: (v: z.infer<typeof schema>) =>
      httpClient<{ data: GeneralSettings }>(generalSettingsKey, {
        method: 'PUT',
        body: JSON.stringify({
          currencyCode: v.currencyCode,
          electricityPricePerKwh:
            v.price.trim() === '' ? null : Number(v.price),
          failureAllowancePercent: Number(v.failure.trim() || 0),
        }),
      }),
    onSuccess: (result) => {
      form.reset(valuesFor(result.data));
      client.setQueryData([generalSettingsKey], result);
      client.setQueryData(['/api/v1/auth/display-preferences'], {
        data: { currencyCode: result.data.currencyCode },
      });
      invalidatePrinterCosts(client);
      toast.success('General settings saved.');
    },
  });
  const priceError = form.formState.errors.price?.message;
  const currencyError = form.formState.errors.currencyCode?.message;
  const failureError = form.formState.errors.failure?.message;
  return (
    <form
      className="flex max-w-[800px] flex-col gap-4"
      onSubmit={form.handleSubmit((v) => {
        if (!save.isPending) save.mutate(v);
      })}
    >
      <FieldGroup>
        <Field data-disabled={save.isPending} data-invalid={!!currencyError}>
          <FieldLabel htmlFor="currency">Currency</FieldLabel>
          <Controller
            control={form.control}
            name="currencyCode"
            render={({ field }) => (
              <Popover open={open} onOpenChange={setOpen}>
                <PopoverTrigger asChild>
                  <Button
                    id="currency"
                    ref={field.ref}
                    type="button"
                    variant="outline"
                    role="combobox"
                    aria-expanded={open}
                    aria-invalid={!!currencyError}
                    aria-describedby="currency-description currency-error"
                    disabled={save.isPending}
                    className="w-full justify-between sm:w-80"
                  >
                    {label(field.value)}
                    <ChevronsUpDownIcon data-icon="inline-end" />
                  </Button>
                </PopoverTrigger>
                <PopoverContent
                  className="w-[var(--radix-popover-trigger-width)] p-0"
                  align="start"
                >
                  <Command>
                    <CommandInput placeholder="Search currencies" />
                    <CommandList>
                      <CommandEmpty>No currencies found.</CommandEmpty>
                      <CommandGroup>
                        {[...data.supportedCurrencies].sort().map((code) => (
                          <CommandItem
                            key={code}
                            value={label(code)}
                            onSelect={() => {
                              field.onChange(code);
                              setOpen(false);
                            }}
                          >
                            {label(code)}
                          </CommandItem>
                        ))}
                      </CommandGroup>
                    </CommandList>
                  </Command>
                </PopoverContent>
              </Popover>
            )}
          />
          <FieldDescription id="currency-description">
            Changing currency relabels existing amounts without converting their
            values.
          </FieldDescription>
          <FieldError id="currency-error">{currencyError}</FieldError>
        </Field>
        <Field data-disabled={save.isPending} data-invalid={!!priceError}>
          <FieldLabel htmlFor="tariff">Electricity rate</FieldLabel>
          <InputGroup className="w-full max-w-64">
            <InputGroupAddon>{currency}</InputGroupAddon>
            <InputGroupInput
              id="tariff"
              inputMode="decimal"
              aria-invalid={!!priceError}
              aria-describedby="tariff-description tariff-error"
              disabled={save.isPending}
              {...form.register('price')}
            />
            <InputGroupAddon align="inline-end">/ kWh</InputGroupAddon>
          </InputGroup>
          <FieldDescription id="tariff-description">
            Used to estimate printing electricity costs. Leave blank if unknown;
            enter 0 for no charge.
          </FieldDescription>
          <FieldError id="tariff-error">{priceError}</FieldError>
        </Field>
        <Field data-disabled={save.isPending} data-invalid={!!failureError}>
          <FieldLabel htmlFor="failure">Failure allowance</FieldLabel>
          <InputGroup className="w-full max-w-40">
            <InputGroupInput
              id="failure"
              inputMode="decimal"
              aria-invalid={!!failureError}
              aria-describedby="failure-description failure-error"
              disabled={save.isPending}
              {...form.register('failure')}
            />
            <InputGroupAddon align="inline-end">%</InputGroupAddon>
          </InputGroup>
          <FieldDescription id="failure-description">
            Adds a buffer to filament, printer depreciation, and electricity
            estimates. Enter 0 for no allowance.
          </FieldDescription>
          <FieldError id="failure-error">{failureError}</FieldError>
        </Field>
      </FieldGroup>
      {save.isError && (
        <ErrorState
          title="Could not save general settings"
          onRetry={() =>
            void form.handleSubmit((v) => {
              if (!save.isPending) save.mutate(v);
            })()
          }
        />
      )}
      <div className="flex flex-wrap items-center gap-2">
        <Button
          permission="settings:general:update"
          type="submit"
          disabled={!form.formState.isDirty || save.isPending}
        >
          {save.isPending && <Spinner data-icon="inline-start" />}
          {save.isPending ? 'Saving…' : 'Save'}
        </Button>
        <Button
          type="button"
          variant="outline"
          disabled={save.isPending}
          onClick={() => {
            form.reset(valuesFor(data));
            save.reset();
          }}
        >
          Reset changes
        </Button>
      </div>
      <ConfirmLeaveDialog />
    </form>
  );
}
