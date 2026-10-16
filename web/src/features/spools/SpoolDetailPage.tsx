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
import type {
  ListSpoolsResponse,
  SpoolResponse,
} from '@/lib/api/generated/endpoints.schemas';
import {
  getOpenFarmApiEndpointsSpoolsListSpoolsEndpointQueryKey,
  useOpenFarmApiEndpointsSpoolsEditSpoolEndpoint,
  useOpenFarmApiEndpointsSpoolsListSpoolsEndpoint,
} from '@/lib/api/generated/spools/spools';
import { formatGrams } from '@/lib/formatUnits';
import { LoadSpoolDialog } from './components/LoadSpoolDialog';
import { RecordUsageDialog } from './components/RecordUsageDialog';
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
  });
}

type FormValues = z.infer<ReturnType<typeof spoolSchema>>;

function toFormValues(spool: SpoolResponse | undefined): FormValues {
  return {
    label: spool?.label ?? '',
    remainingWeightGrams: String(spool?.remainingWeightGrams ?? ''),
  };
}

export function SpoolDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [dialog, setDialog] = useState<'load' | 'unload' | 'usage' | null>(
    null,
  );

  // ponytail: no GET /spools/{id} endpoint — the detail reads from the (unpaged) list query.
  const spoolsQuery = useOpenFarmApiEndpointsSpoolsListSpoolsEndpoint();
  const spool = (
    spoolsQuery.data?.data as ListSpoolsResponse | undefined
  )?.spools?.find((candidate) => candidate.id === id);
  const capacity = spool?.spoolWeightGrams ?? 0;

  // `values` re-syncs the draft when Load/Unload/Record usage refetch the list; keepDirtyValues
  // keeps whatever the operator is mid-way through editing.
  const form = useForm<FormValues>({
    resolver: zodResolver(spoolSchema(capacity)),
    values: toFormValues(spool),
    resetOptions: { keepDirtyValues: true },
  });
  const { isDirty } = form.formState;
  const { ConfirmLeaveDialog } = useUnsavedChangesPrompt(isDirty);

  const editMutation = useOpenFarmApiEndpointsSpoolsEditSpoolEndpoint({
    mutation: {
      onSuccess: (response) => {
        form.reset(toFormValues(response.data as SpoolResponse));
        queryClient.invalidateQueries({
          queryKey: getOpenFarmApiEndpointsSpoolsListSpoolsEndpointQueryKey(),
        });
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
      },
    });
  }

  const backLink = (
    <BackLink to="/spools" className="mb-1">
      Back to spools
    </BackLink>
  );

  if (spoolsQuery.isLoading || spoolsQuery.isError || !spool) {
    return (
      <Page>
        <PageHeader above={backLink} title="Spool" />
        {spoolsQuery.isLoading ? (
          <Skeleton className="h-64 w-full" />
        ) : spoolsQuery.isError ? (
          <ErrorState
            title="Could not load spool"
            onRetry={() => spoolsQuery.refetch()}
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
              </CardContent>
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
