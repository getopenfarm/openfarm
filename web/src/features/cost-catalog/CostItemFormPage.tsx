import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Trash2Icon } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { useForm, useWatch } from 'react-hook-form';
import { useNavigate, useParams } from 'react-router';
import { toast } from 'sonner';
import { BackLink } from '@/components/back-link/BackLink';
import { ConfirmDialog } from '@/components/confirm-dialog/ConfirmDialog';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { FormFooter } from '@/components/form-footer/FormFooter';
import { PermissionButton as Button } from '@/components/PermissionButton';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import {
  Field,
  FieldError,
  FieldGroup,
  FieldLabel,
} from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { Skeleton } from '@/components/ui/skeleton';
import { Spinner } from '@/components/ui/spinner';
import { useCurrency } from '@/features/settings/generalSettings';
import { useUnsavedChangesPrompt } from '@/hooks/useUnsavedChangesPrompt';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import type {
  CatalogPricing,
  CostItemResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { httpClient } from '@/lib/api/httpClient';
import { useAuthorizedQuery as useQuery } from '@/lib/auth/useAuthorizedQuery';
import { catalogKey } from './api';
import { Choice, DimensionFields, NumberField, UnitSelect } from './CostFields';
import { CostIconPicker } from './CostIconPicker';
import { CostPricingSummary } from './CostPricingSummary';
import {
  type CostItemFormValues,
  costItemSchema,
  formValues,
  pricingPayload,
} from './costItemForm';
import { categories, categoryLabel, defaultUnit, isService } from './costing';

export function CostItemFormPage({ mode }: { mode: 'add' | 'edit' }) {
  const currency = useCurrency();
  const { id } = useParams();
  const navigate = useNavigate();
  const client = useQueryClient();
  const form = useForm<CostItemFormValues>({
    resolver: zodResolver(costItemSchema),
    defaultValues: formValues(),
  });
  const pricing = useWatch({ control: form.control, name: 'pricing' });
  const measurement = useWatch({
    control: form.control,
    name: 'packageMeasurement',
  });
  const icon = useWatch({ control: form.control, name: 'icon' });
  const { errors, isDirty, isSubmitted } = form.formState;
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  const seeded = useRef<string | null>(null);
  const formRef = useRef<HTMLFormElement>(null);
  const query = useQuery({
    queryKey: [catalogKey, id],
    enabled: mode === 'edit',
    queryFn: ({ signal }) =>
      httpClient<{ data: CostItemResponse }>(`${catalogKey}/${id}`, { signal }),
  });
  const item = query.data?.data;
  useEffect(() => {
    if (item?.id && item.id !== seeded.current) {
      seeded.current = item.id;
      form.reset(formValues(item));
    }
  }, [item, form]);
  const { ConfirmLeaveDialog, markClean } = useUnsavedChangesPrompt(isDirty);
  const patch = (next: Partial<CatalogPricing>) =>
    form.setValue(
      'pricing',
      { ...form.getValues('pricing'), ...next },
      { shouldDirty: true, shouldValidate: isSubmitted },
    );
  const pricingError = (field: keyof CatalogPricing) =>
    errors.pricing?.[field]?.message;
  const invalidate = () => {
    void client.invalidateQueries({ queryKey: [catalogKey] });
    void client.invalidateQueries({
      queryKey: ['/api/v1/non-printed-components'],
    });
    void client.invalidateQueries({
      predicate: (query) =>
        String(query.queryKey[0]).startsWith('/api/v1/assemblies'),
    });
  };
  const save = useMutation({
    mutationFn: (values: CostItemFormValues) =>
      httpClient<{ data: CostItemResponse }>(
        `${catalogKey}${mode === 'edit' ? `/${id}` : ''}`,
        {
          method: mode === 'edit' ? 'PUT' : 'POST',
          body: JSON.stringify({
            name: values.name.trim(),
            icon: values.icon,
            pricing: pricingPayload(values.pricing, values.packageMeasurement),
          }),
        },
      ),
    onSuccess: (response) => {
      invalidate();
      markClean();
      form.reset(formValues(response.data));
      setSaveError(null);
      toast.success(mode === 'add' ? 'Cost item added.' : 'Cost item saved.');
      if (mode === 'add')
        navigate(
          response.data?.id
            ? `/cost-catalog/${response.data.id}`
            : '/cost-catalog',
        );
      else client.setQueryData([catalogKey, id], response);
    },
    onError: async (error) =>
      setSaveError(await extractErrorDetail(error, 'Save failed.')),
  });
  const remove = useMutation({
    mutationFn: () => httpClient(`${catalogKey}/${id}`, { method: 'DELETE' }),
    onSuccess: () => {
      invalidate();
      markClean();
      toast.success('Cost item removed.');
      navigate('/cost-catalog');
    },
    onError: async (error) =>
      toast.error(await extractErrorDetail(error, 'Removal failed.')),
  });
  const busy = save.isPending || remove.isPending;
  const unit = pricing.unit ?? 'Piece';
  const service = isService(unit);
  const squareMetres = unit === 'SquareMetre';
  const sized = squareMetres && measurement === 'Dimensions';
  const back = <BackLink to="/cost-catalog">Back to cost catalog</BackLink>;
  const backAction = (
    <Button variant="outline" onClick={() => navigate('/cost-catalog')}>
      Back to cost catalog
    </Button>
  );
  if (mode === 'edit' && (!item || seeded.current !== item.id))
    return (
      <Page>
        <PageHeader above={back} title="Cost item" />
        {query.isPending || item ? (
          <Skeleton className="h-64 w-full" />
        ) : (query.error as { status?: number })?.status === 404 ? (
          <EmptyState
            title="Cost item not found"
            description="Removed or invalid link."
            action={backAction}
          />
        ) : (
          <ErrorState title="Load failed" onRetry={() => query.refetch()} />
        )}
      </Page>
    );

  // Labor and shipping are services: move a unit still at the old category's
  // default to the new one, but never overwrite a unit chosen on purpose.
  function setCategory(category: string) {
    const untouched = unit === defaultUnit(pricing.category);
    patch({ category, ...(untouched ? { unit: defaultUnit(category) } : {}) });
  }
  function setMeasurement(next: 'Quantity' | 'Dimensions') {
    form.setValue('packageMeasurement', next, {
      shouldDirty: true,
      shouldValidate: isSubmitted,
    });
  }
  return (
    <Page className="flex-1">
      <PageHeader
        above={back}
        title={mode === 'add' ? 'Add cost item' : (item?.name ?? 'Cost item')}
        action={
          mode === 'edit' ? (
            <Button
              permission="non-printed-components:delete"
              type="button"
              variant="destructive"
              disabled={busy}
              onClick={() => setConfirmOpen(true)}
            >
              <Trash2Icon data-icon="inline-start" />
              Remove
            </Button>
          ) : undefined
        }
      />
      <form
        noValidate
        ref={formRef}
        className="flex flex-1 flex-col gap-6"
        aria-busy={busy}
        onSubmit={form.handleSubmit(
          (values) => {
            if (!busy) save.mutate(values);
          },
          () =>
            requestAnimationFrame(() =>
              formRef.current
                ?.querySelector<HTMLElement>('[aria-invalid="true"]')
                ?.focus(),
            ),
        )}
      >
        {saveError && (
          <Alert variant="destructive" role="alert">
            <AlertTitle>Save failed</AlertTitle>
            <AlertDescription>{saveError}</AlertDescription>
          </Alert>
        )}
        <FieldGroup className="flex-1">
          <fieldset disabled={busy} className="flex min-w-0 flex-col gap-6">
            <Card>
              <CardHeader>
                <CardTitle>Cost item</CardTitle>
              </CardHeader>
              <CardContent className="grid grid-cols-1 items-start gap-4 sm:grid-cols-2 lg:grid-cols-4">
                <Field
                  className="sm:col-span-2"
                  data-invalid={!!errors.name || undefined}
                >
                  <FieldLabel htmlFor="cost-name">Name</FieldLabel>
                  <div className="flex gap-2">
                    <CostIconPicker
                      icon={icon}
                      category={pricing.category}
                      onChange={(next) =>
                        form.setValue('icon', next, { shouldDirty: true })
                      }
                    />
                    <Input
                      id="cost-name"
                      placeholder={service ? 'Assembly labor' : 'Bubble wrap'}
                      {...form.register('name')}
                      aria-invalid={!!errors.name || undefined}
                      aria-describedby={
                        errors.name ? 'cost-name-error' : undefined
                      }
                    />
                  </div>
                  {errors.name && (
                    <FieldError id="cost-name-error">
                      {errors.name.message}
                    </FieldError>
                  )}
                </Field>
                <Choice
                  label="Category"
                  value={pricing.category ?? 'Hardware'}
                  options={categories.map((value) => ({
                    value,
                    label: categoryLabel(value),
                  }))}
                  onChange={setCategory}
                  disabled={busy}
                />
              </CardContent>
            </Card>
            <Card>
              <CardHeader>
                <CardTitle>Pricing</CardTitle>
              </CardHeader>
              <CardContent>
                <FieldGroup className="gap-3">
                  <div className="flex flex-wrap items-start gap-4">
                    <NumberField
                      label="Price"
                      description={`Currency: ${currency}`}
                      className="w-full sm:w-44"
                      value={pricing.packagePrice}
                      onChange={(packagePrice) => patch({ packagePrice })}
                      error={pricingError('packagePrice')}
                      disabled={busy}
                    />
                    {service ? (
                      <Field className="w-full sm:w-40">
                        <FieldLabel htmlFor="cost-unit">Unit</FieldLabel>
                        <UnitSelect
                          id="cost-unit"
                          value={unit}
                          onChange={(next) => patch({ unit: next })}
                          disabled={busy}
                        />
                      </Field>
                    ) : (
                      <div className="flex w-full flex-col gap-2 sm:w-auto">
                        {sized ? (
                          <DimensionFields
                            className="sm:w-[29rem]"
                            {...pricing}
                            onChange={patch}
                            errors={{
                              length: pricingError('length'),
                              width: pricingError('width'),
                            }}
                          />
                        ) : (
                          <NumberField
                            label="Quantity"
                            className="sm:w-60"
                            placeholder="1"
                            whole={unit === 'Piece'}
                            value={pricing.packageQuantity}
                            onChange={(packageQuantity) =>
                              patch({ packageQuantity })
                            }
                            error={pricingError('packageQuantity')}
                            disabled={busy}
                            adornment={
                              <UnitSelect
                                value={unit}
                                onChange={(next) => patch({ unit: next })}
                                disabled={busy}
                              />
                            }
                          />
                        )}
                        {squareMetres && (
                          <Button
                            type="button"
                            variant="link"
                            size="sm"
                            className="h-auto self-start p-0 text-xs"
                            disabled={busy}
                            onClick={() =>
                              setMeasurement(sized ? 'Quantity' : 'Dimensions')
                            }
                          >
                            {sized ? 'Use area' : 'Use length × width'}
                          </Button>
                        )}
                      </div>
                    )}
                  </div>
                  <CostPricingSummary
                    pricing={pricingPayload(pricing, measurement)}
                  />
                </FieldGroup>
              </CardContent>
            </Card>
          </fieldset>
        </FieldGroup>
        <FormFooter
          end={
            <>
              {isDirty && (
                <span className="text-sm text-muted-foreground">
                  Unsaved changes
                </span>
              )}
              <Button
                type="button"
                variant="outline"
                disabled={busy}
                onClick={() => navigate('/cost-catalog')}
              >
                Cancel
              </Button>
              <Button
                permission={
                  mode === 'add'
                    ? 'non-printed-components:create'
                    : 'non-printed-components:edit'
                }
                type="submit"
                disabled={busy || (mode === 'edit' && !isDirty)}
              >
                {save.isPending && <Spinner data-icon="inline-start" />}
                {save.isPending
                  ? 'Saving…'
                  : mode === 'add'
                    ? 'Add cost item'
                    : 'Save'}
              </Button>
            </>
          }
        />
      </form>
      <ConfirmDialog
        open={confirmOpen}
        onOpenChange={setConfirmOpen}
        title="Remove cost item?"
        description={item?.name ?? 'Cost item'}
        confirmLabel={remove.isPending ? 'Removing…' : 'Remove'}
        closeOnConfirm={false}
        destructive
        isPending={remove.isPending}
        onConfirm={() => remove.mutate()}
      />
      <ConfirmLeaveDialog />
    </Page>
  );
}
