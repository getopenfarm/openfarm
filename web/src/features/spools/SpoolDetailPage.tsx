import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { useNavigate, useParams } from 'react-router';
import { toast } from 'sonner';
import { z } from 'zod';
import { BackLink } from '@/components/back-link/BackLink';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { FormFooter } from '@/components/form-footer/FormFooter';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import { StatusBadge } from '@/components/status-badge/StatusBadge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import {
  Form,
  FormControl,
  FormDescription,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from '@/components/ui/form';
import { Input } from '@/components/ui/input';
import { Skeleton } from '@/components/ui/skeleton';
import { Spinner } from '@/components/ui/spinner';
import { useUnsavedChangesPrompt } from '@/hooks/useUnsavedChangesPrompt';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import type { SpoolResponse } from '@/lib/api/generated/endpoints.schemas';
import {
  useOpenFarmApiEndpointsSpoolsEditSpoolEndpoint,
  useOpenFarmApiEndpointsSpoolsGetSpoolEndpoint,
} from '@/lib/api/generated/spools/spools';
import { formatCost, formatGrams } from '@/lib/formatUnits';
import { invalidateFilamentTrackingQueries } from '@/lib/invalidateFilamentTrackingQueries';
import { LoadSpoolDialog } from './components/LoadSpoolDialog';
import { RecordUsageDialog } from './components/RecordUsageDialog';
import { SpoolHistoryCard } from './components/SpoolHistoryCard';
import { STATE_BADGE } from './components/SpoolsTable';
import { UnloadSpoolDialog } from './components/UnloadSpoolDialog';

function spoolSchema(capacity: number) {
  return z.object({
    label: z.string(),
    remainingWeightGrams: z
      .string()
      .trim()
      .min(1, 'Enter the remaining weight')
      .refine((value) => Number(value) >= 0, 'Must be 0 or more')
      .refine(
        (value) => Number(value) <= capacity,
        `Cannot exceed the spool capacity (${capacity} g).`,
      ),
    reason: z.string().max(200, 'Up to 200 characters'),
    price: z
      .string()
      .refine(
        (value) => value.trim() === '' || Number(value) >= 0,
        'Must be 0 or more',
      ),
    supplier: z.string().max(100, 'Up to 100 characters'),
    orderNumber: z.string().max(100, 'Up to 100 characters'),
    receivedOn: z.string(),
  });
}

type FormValues = z.infer<ReturnType<typeof spoolSchema>>;

function toFormValues(spool: SpoolResponse | undefined): FormValues {
  return {
    label: spool?.label ?? '',
    remainingWeightGrams: String(spool?.remainingWeightGrams ?? ''),
    reason: '',
    price: spool?.price != null ? String(spool.price) : '',
    supplier: spool?.supplier ?? '',
    orderNumber: spool?.orderNumber ?? '',
    receivedOn: spool?.receivedOn ?? '',
  };
}

export function SpoolDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [dialog, setDialog] = useState<'load' | 'unload' | 'usage' | null>(
    null,
  );

  const spoolQuery = useOpenFarmApiEndpointsSpoolsGetSpoolEndpoint(id ?? '');
  const spool = spoolQuery.data?.data as SpoolResponse | undefined;
  const capacity = spool?.spoolWeightGrams ?? 0;

  // `values` re-syncs the draft when Load/Unload/Record usage refetch the spool; keepDirtyValues
  // keeps whatever the operator is mid-way through editing.
  const form = useForm<FormValues>({
    resolver: zodResolver(spoolSchema(capacity)),
    values: toFormValues(spool),
    resetOptions: { keepDirtyValues: true },
  });
  const { isDirty } = form.formState;
  const { ConfirmLeaveDialog } = useUnsavedChangesPrompt(isDirty);

  // A reason only means something for a weight change, so the field appears once the draft weight differs.
  const draftWeight = form.watch('remainingWeightGrams').trim();
  const weightChanged =
    draftWeight !== '' && Number(draftWeight) !== spool?.remainingWeightGrams;

  // The price is for the whole spool, so a per-kg figure beside it is what compares with the catalog's price per kg.
  const draftPrice = form.watch('price').trim();
  const pricePerKg =
    draftPrice !== '' && Number(draftPrice) >= 0 && capacity > 0
      ? Number(draftPrice) / (capacity / 1000)
      : null;

  const editMutation = useOpenFarmApiEndpointsSpoolsEditSpoolEndpoint({
    mutation: {
      onSuccess: (response) => {
        form.reset(toFormValues(response.data as SpoolResponse));
        invalidateFilamentTrackingQueries(queryClient);
        toast.success('Spool saved.');
      },
      onError: async (error) =>
        toast.error(await extractErrorDetail(error, 'Could not save spool.')),
    },
  });

  function onSubmit(values: FormValues) {
    if (!spool?.id) return;
    editMutation.mutate({
      id: spool.id,
      data: {
        label: values.label.trim() || null,
        remainingWeightGrams: Number(values.remainingWeightGrams),
        reason: weightChanged ? values.reason.trim() || null : null,
        price: values.price.trim() === '' ? null : Number(values.price),
        supplier: values.supplier.trim() || null,
        orderNumber: values.orderNumber.trim() || null,
        receivedOn: values.receivedOn || null,
      },
    });
  }

  const backLink = (
    <BackLink to="/spools" className="mb-1">
      Back to spools
    </BackLink>
  );

  const queryError: unknown = spoolQuery.error;
  const loadFailed =
    spoolQuery.isError &&
    !(queryError instanceof Response && queryError.status === 404);
  if (spoolQuery.isLoading || loadFailed || !spool) {
    return (
      <Page>
        <PageHeader above={backLink} title="Spool" />
        {spoolQuery.isLoading ? (
          <Skeleton className="h-64 w-full" />
        ) : loadFailed ? (
          <ErrorState
            title="Could not load spool"
            onRetry={() => spoolQuery.refetch()}
          />
        ) : (
          <EmptyState
            title="Spool not found"
            description="Removed or invalid link."
            action={
              <Button variant="outline" onClick={() => navigate('/spools')}>
                Back to spools
              </Button>
            }
          />
        )}
      </Page>
    );
  }

  const state = STATE_BADGE[spool.state ?? ''] ?? {
    kind: 'zinc' as const,
    label: spool.state ?? '',
  };
  const spoolId = spool.id ?? '';

  return (
    <Page className="flex-1">
      <PageHeader
        above={backLink}
        title={spool.label || spool.colorName}
        description={
          <span className="flex flex-wrap items-center gap-2">
            {spool.vendorName} · {spool.material} · {spool.colorName} ·{' '}
            {formatGrams(spool.spoolWeightGrams ?? 0)} capacity
            <StatusBadge kind={state.kind} label={state.label} />
            {spool.feedSlotLabel && (
              <span className="font-mono text-xs">{spool.feedSlotLabel}</span>
            )}
          </span>
        }
        action={
          spool.state === 'InStorage' ? (
            <Button variant="outline" onClick={() => setDialog('load')}>
              Load
            </Button>
          ) : spool.state === 'Using' ? (
            <div className="flex flex-wrap items-center gap-2">
              <Button variant="outline" onClick={() => setDialog('usage')}>
                Record usage
              </Button>
              <Button variant="outline" onClick={() => setDialog('unload')}>
                Unload
              </Button>
            </div>
          ) : null
        }
      />

      <Form {...form}>
        <form
          onSubmit={form.handleSubmit(onSubmit)}
          className="flex flex-1 flex-col gap-6"
        >
          <div className="flex flex-1 flex-col gap-6">
            <Card>
              <CardHeader>
                <CardTitle>Spool</CardTitle>
              </CardHeader>
              <CardContent className="flex flex-col gap-4">
                <FormField
                  control={form.control}
                  name="label"
                  render={({ field }) => (
                    <FormItem>
                      <FormLabel>Label</FormLabel>
                      <FormControl>
                        <Input placeholder={spool.colorName} {...field} />
                      </FormControl>
                      <FormMessage />
                    </FormItem>
                  )}
                />
                <FormField
                  control={form.control}
                  name="remainingWeightGrams"
                  render={({ field }) => (
                    <FormItem>
                      <FormLabel>Remaining weight (g)</FormLabel>
                      <FormControl>
                        <Input type="number" {...field} />
                      </FormControl>
                      <FormMessage />
                    </FormItem>
                  )}
                />
                {weightChanged && (
                  <FormField
                    control={form.control}
                    name="reason"
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel>Reason (optional)</FormLabel>
                        <FormControl>
                          <Input
                            placeholder="e.g. weighed on scale"
                            {...field}
                          />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                )}
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle>Purchase</CardTitle>
              </CardHeader>
              <CardContent className="flex flex-col gap-4">
                <FormField
                  control={form.control}
                  name="price"
                  render={({ field }) => (
                    <FormItem>
                      <FormLabel>Price paid (per spool)</FormLabel>
                      <FormControl>
                        <Input type="number" step="any" {...field} />
                      </FormControl>
                      {pricePerKg !== null && (
                        <FormDescription>
                          {formatCost(pricePerKg)} per kg
                        </FormDescription>
                      )}
                      <FormMessage />
                    </FormItem>
                  )}
                />
                <FormField
                  control={form.control}
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
                  control={form.control}
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
                  control={form.control}
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
              </CardContent>
            </Card>

            <SpoolHistoryCard spoolId={spoolId} />

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
                  <Button
                    type="submit"
                    disabled={!isDirty || editMutation.isPending}
                  >
                    {editMutation.isPending && (
                      <Spinner data-icon="inline-start" />
                    )}
                    {editMutation.isPending ? 'Saving…' : 'Save'}
                  </Button>
                </>
              }
            />
          </div>
        </form>
      </Form>

      {dialog === 'load' && (
        <LoadSpoolDialog
          spoolId={spoolId}
          open
          onOpenChange={(open) => !open && setDialog(null)}
        />
      )}
      {dialog === 'unload' && (
        <UnloadSpoolDialog
          spoolId={spoolId}
          open
          onOpenChange={(open) => !open && setDialog(null)}
        />
      )}
      {dialog === 'usage' && (
        <RecordUsageDialog
          spoolId={spoolId}
          open
          onOpenChange={(open) => !open && setDialog(null)}
        />
      )}
      <ConfirmLeaveDialog />
    </Page>
  );
}
