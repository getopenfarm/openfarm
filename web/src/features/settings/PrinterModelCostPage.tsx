import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef } from 'react';
import { Controller, useForm, useWatch } from 'react-hook-form';
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
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card';
import {
  Field,
  FieldError,
  FieldGroup,
  FieldLabel,
} from '@/components/ui/field';
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
import { useCurrency } from '@/features/settings/generalSettings';
import { useUnsavedChangesPrompt } from '@/hooks/useUnsavedChangesPrompt';
import { httpClient } from '@/lib/api/httpClient';
import { formatCost } from '@/lib/formatUnits';
import { optionalCostNumber } from './PrinterCostsPage';
import {
  invalidatePrinterCosts,
  modelKey,
  printerCostsKey,
  usePrinterCostCatalog,
  usePrinterCosts,
} from './printerCosts';

const schema = z.object({
  model: z.string().min(1, 'Select a printer model.'),
  printerPurchasePrice: optionalCostNumber,
  printerUpfrontCost: optionalCostNumber,
  lifetimeServiceCost: optionalCostNumber,
  lifetimePrintingHours: optionalCostNumber.refine(
    (v) => v.trim() === '' || Number(v) > 0,
    'Enter positive printing hours.',
  ),
  averagePrintingWatts: optionalCostNumber,
});
type Values = z.infer<typeof schema>;
const fields = [
  ['printerPurchasePrice', 'Printer purchase price', ''],
  ['printerUpfrontCost', 'Printer upfront cost', ''],
  ['lifetimeServiceCost', 'Lifetime service cost', ''],
  ['lifetimePrintingHours', 'Lifetime printing hours', 'h'],
  ['averagePrintingWatts', 'Average printing power', 'W'],
] as const;
const empty: Values = {
  model: '',
  printerPurchasePrice: '',
  printerUpfrontCost: '',
  lifetimeServiceCost: '',
  lifetimePrintingHours: '',
  averagePrintingWatts: '',
};
const number = (v: string) => (v.trim() === '' ? null : Number(v));

export function PrinterModelCostPage() {
  const currency = useCurrency();
  const { brand, modelCode } = useParams();
  const editing = !!brand && !!modelCode;
  const key = modelKey(brand, modelCode);
  const costs = usePrinterCosts();
  const catalog = usePrinterCostCatalog();
  const client = useQueryClient();
  const navigate = useNavigate();
  const seeded = useRef('');
  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: empty,
  });
  const values = useWatch({ control: form.control });
  const saved = costs.data?.data.models?.find(
    (m) => modelKey(m.brand, m.modelCode) === key,
  );
  useEffect(() => {
    if (!editing || !saved || seeded.current === key) return;
    seeded.current = key;
    form.reset({
      model: key,
      ...Object.fromEntries(
        fields.map(([name]) => [
          name,
          saved[name] == null ? '' : String(saved[name]),
        ]),
      ),
    } as Values);
  }, [editing, saved, key, form]);
  const { ConfirmLeaveDialog, markClean } = useUnsavedChangesPrompt(
    form.formState.isDirty,
  );
  const save = useMutation({
    mutationFn: (v: Values) => {
      const [selectedBrand, selectedModel] = v.model.split('/');
      return httpClient(`${printerCostsKey}/models`, {
        method: 'PUT',
        body: JSON.stringify({
          brand: selectedBrand,
          modelCode: selectedModel,
          ...Object.fromEntries(
            fields.map(([name]) => [name, number(v[name])]),
          ),
        }),
      });
    },
    onSuccess: () => {
      markClean();
      invalidatePrinterCosts(client);
      toast.success('Printer costs saved.');
      navigate('/settings/printer-costs');
    },
  });
  const models = (catalog.data?.data.brands ?? []).flatMap((b) =>
    (b.models ?? []).map((m) => ({
      key: modelKey(b.brand, m.modelCode),
      name: m.name ?? `${b.brand} ${m.modelCode}`,
    })),
  );
  const available = models.filter(
    (m) =>
      editing ||
      !costs.data?.data.models?.some(
        (c) => modelKey(c.brand, c.modelCode) === m.key,
      ),
  );
  const operands = [
    values.printerPurchasePrice,
    values.printerUpfrontCost,
    values.lifetimeServiceCost,
  ];
  const depreciation =
    operands.every(
      (v) => v?.trim() && Number.isFinite(Number(v)) && Number(v) >= 0,
    ) && Number(values.lifetimePrintingHours) > 0
      ? operands.reduce((s, v) => s + Number(v), 0) /
        Number(values.lifetimePrintingHours)
      : null;
  const watts = values.averagePrintingWatts?.trim()
    ? Number(values.averagePrintingWatts)
    : null;
  const tariff = costs.data?.data.electricityPricePerKwh;
  const electricity =
    watts != null && Number.isFinite(watts) && watts >= 0 && tariff != null
      ? (watts / 1000) * tariff
      : null;
  const title = editing ? 'Printer model costs' : 'Add printer model';
  return (
    <Page>
      <PageHeader
        title={title}
        above={<BackLink to="/settings/printer-costs">Printer costs</BackLink>}
      />
      {costs.isPending || catalog.isPending ? (
        <Skeleton className="h-60" />
      ) : costs.isError || catalog.isError ? (
        <ErrorState
          title="Could not load printer costs"
          onRetry={() => {
            void costs.refetch();
            void catalog.refetch();
          }}
        />
      ) : editing && !saved ? (
        <EmptyState
          title="Printer model not found"
          action={
            <Button asChild>
              <Link to="/settings/printer-costs">Back to printer costs</Link>
            </Button>
          }
        />
      ) : (
        <form
          onSubmit={form.handleSubmit((v) => save.mutate(v))}
          className="flex flex-col gap-6"
        >
          <Card>
            <CardHeader>
              <CardTitle>Printer model</CardTitle>
              <CardDescription>
                Costs are shared by all printers of this model.
              </CardDescription>
            </CardHeader>
            <CardContent>
              <FieldGroup>
                <Field data-invalid={!!form.formState.errors.model}>
                  <FieldLabel htmlFor="cost-model">Model</FieldLabel>
                  <Controller
                    name="model"
                    control={form.control}
                    render={({ field }) =>
                      editing ? (
                        <Input
                          id="cost-model"
                          readOnly
                          value={models.find((m) => m.key === key)?.name ?? key}
                        />
                      ) : (
                        <Select
                          value={field.value}
                          onValueChange={field.onChange}
                          disabled={editing || save.isPending}
                        >
                          <SelectTrigger
                            id="cost-model"
                            ref={field.ref}
                            onBlur={field.onBlur}
                            aria-invalid={!!form.formState.errors.model}
                            aria-describedby="model-error"
                          >
                            <SelectValue placeholder="Select a printer model" />
                          </SelectTrigger>
                          <SelectContent>
                            <SelectGroup>
                              {available.map((m) => (
                                <SelectItem key={m.key} value={m.key}>
                                  {m.name}
                                </SelectItem>
                              ))}
                            </SelectGroup>
                          </SelectContent>
                        </Select>
                      )
                    }
                  />
                  <FieldError id="model-error">
                    {form.formState.errors.model?.message}
                  </FieldError>
                </Field>
              </FieldGroup>
            </CardContent>
          </Card>
          <Card>
            <CardHeader>
              <CardTitle>Cost assumptions</CardTitle>
              <CardDescription>
                Depreciation includes purchase price, upfront costs, and
                lifetime service. Use average printing power rather than peak
                power.
              </CardDescription>
            </CardHeader>
            <CardContent>
              <FieldGroup className="grid grid-cols-2 gap-4 lg:grid-cols-3">
                {fields.map(([name, label, unit]) => (
                  <Field
                    key={name}
                    data-invalid={!!form.formState.errors[name]}
                  >
                    <FieldLabel htmlFor={name}>{label}</FieldLabel>
                    <InputGroup>
                      <InputGroupInput
                        id={name}
                        inputMode="decimal"
                        disabled={save.isPending}
                        aria-invalid={!!form.formState.errors[name]}
                        aria-describedby={`${name}-error`}
                        {...form.register(name)}
                      />
                      {(unit || currency) && (
                        <InputGroupAddon align="inline-end">
                          {unit || currency}
                        </InputGroupAddon>
                      )}
                    </InputGroup>
                    <FieldError id={`${name}-error`}>
                      {form.formState.errors[name]?.message}
                    </FieldError>
                  </Field>
                ))}
              </FieldGroup>
              <dl className="mt-6 flex flex-wrap gap-6">
                <div>
                  <dt>Depreciation / h</dt>
                  <dd>
                    {depreciation == null
                      ? 'Not priced'
                      : formatCost(depreciation, currency)}
                  </dd>
                </div>
                <div>
                  <dt>Electricity / h</dt>
                  <dd>
                    {electricity == null
                      ? 'Not priced'
                      : formatCost(electricity, currency)}
                  </dd>
                </div>
              </dl>
            </CardContent>
          </Card>
          {save.isError && (
            <ErrorState
              title="Could not save printer costs"
              onRetry={() => void form.handleSubmit((v) => save.mutate(v))()}
            />
          )}
          <FormFooter
            end={
              <>
                <Button variant="outline" asChild disabled={save.isPending}>
                  <Link
                    to="/settings/printer-costs"
                    aria-disabled={save.isPending}
                    onClick={(event) => {
                      if (save.isPending) event.preventDefault();
                    }}
                  >
                    Cancel
                  </Link>
                </Button>
                <Button type="submit" disabled={save.isPending}>
                  {save.isPending && <Spinner data-icon="inline-start" />}
                  {save.isPending ? 'Saving…' : 'Save printer costs'}
                </Button>
              </>
            }
          />
        </form>
      )}
      <ConfirmLeaveDialog />
    </Page>
  );
}
