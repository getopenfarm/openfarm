import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { useForm } from 'react-hook-form';
import { useNavigate, useParams } from 'react-router';
import { toast } from 'sonner';
import { z } from 'zod';
import { BackLink } from '@/components/back-link/BackLink';
import { EmptyState } from '@/components/empty-state/EmptyState';
import { FormFooter } from '@/components/form-footer/FormFooter';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Badge } from '@/components/ui/badge';
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
import type { ProductionRunDetailResponse } from '@/lib/api/generated/endpoints.schemas';
import {
  getOpenFarmApiEndpointsProductionRunsGetProductionRunEndpointQueryKey,
  getOpenFarmApiEndpointsProductionRunsListProductionRunsEndpointQueryKey,
  useOpenFarmApiEndpointsProductionRunsGetProductionRunEndpoint,
  useOpenFarmApiEndpointsProductionRunsSetRunBackfillLimitsEndpoint,
} from '@/lib/api/generated/production-runs/production-runs';

const wholeNumber = (min: number, message: string) =>
  z
    .string()
    .refine((v) => Number.isInteger(Number(v)) && Number(v) >= min, message);

const schema = z.object({
  lines: z.array(
    z.object({
      lineId: z.string(),
      backfillPrinterCap: wholeNumber(
        1,
        'Must be a whole number of 1 or more.',
      ),
      backfillCumulativeUnitCeiling: z
        .string()
        .refine(
          (v) =>
            v.trim() === '' || (Number.isInteger(Number(v)) && Number(v) >= 0),
          'Must be 0 or more, or blank for no ceiling.',
        ),
    }),
  ),
});

type FormValues = z.infer<typeof schema>;

function unlimitedLines(run: ProductionRunDetailResponse | undefined) {
  return (run?.lines ?? []).filter((l) => l.quantity == null && l.id);
}

function toFormValues(run: ProductionRunDetailResponse): FormValues {
  return {
    lines: unlimitedLines(run).map((l) => ({
      lineId: l.id ?? '',
      backfillPrinterCap: String(l.backfillPrinterCap ?? 1),
      backfillCumulativeUnitCeiling:
        l.backfillCumulativeUnitCeiling != null
          ? String(l.backfillCumulativeUnitCeiling)
          : '',
    })),
  };
}

/** Edits the backfill limits of a run's unlimited lines as one draft, saved in one call. */
export function ProductionRunEditPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const runQuery =
    useOpenFarmApiEndpointsProductionRunsGetProductionRunEndpoint(id ?? '');
  const run = runQuery.data?.data;

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { lines: [] },
  });
  const { isDirty } = form.formState;

  // biome-ignore lint/correctness/useExhaustiveDependencies: seed once per run, not on every hub-driven refetch (which would clobber the draft).
  useEffect(() => {
    if (run) form.reset(toFormValues(run));
  }, [run?.id]);

  const { ConfirmLeaveDialog } = useUnsavedChangesPrompt(isDirty);

  const mutation =
    useOpenFarmApiEndpointsProductionRunsSetRunBackfillLimitsEndpoint({
      mutation: {
        onSuccess: (response) => {
          if (response.status === 200) form.reset(toFormValues(response.data));
          queryClient.invalidateQueries({
            queryKey:
              getOpenFarmApiEndpointsProductionRunsListProductionRunsEndpointQueryKey(),
          });
          if (id)
            queryClient.invalidateQueries({
              queryKey:
                getOpenFarmApiEndpointsProductionRunsGetProductionRunEndpointQueryKey(
                  id,
                ),
            });
          toast.success('Production run saved.');
        },
        onError: async (error) => {
          toast.error(
            await extractErrorDetail(error, 'Could not save backfill limits.'),
          );
        },
      },
    });

  const backLink = (
    <BackLink to={`/production-runs/${id}`} className="mb-1">
      Back to production run
    </BackLink>
  );

  if (!run) {
    return (
      <Page>
        <PageHeader above={backLink} title="Edit production run" />
        {runQuery.isLoading ? (
          <Skeleton className="h-64 w-full" />
        ) : (
          <EmptyState
            title="Production run not found"
            description="Removed or invalid link."
            action={
              <Button
                variant="outline"
                onClick={() => navigate('/production-runs')}
              >
                Back to production runs
              </Button>
            }
          />
        )}
      </Page>
    );
  }

  const lines = unlimitedLines(run);
  const pending = mutation.isPending;

  function onSubmit(values: FormValues) {
    if (!id) return;
    mutation.mutate({
      id,
      data: {
        lines: values.lines.map((l) => ({
          lineId: l.lineId,
          backfillPrinterCap: Number(l.backfillPrinterCap),
          backfillCumulativeUnitCeiling:
            l.backfillCumulativeUnitCeiling.trim() === ''
              ? null
              : Number(l.backfillCumulativeUnitCeiling),
        })),
      },
    });
  }

  return (
    <Page className="flex-1">
      <PageHeader
        above={backLink}
        title={`Edit ${run.name ?? run.lines?.[0]?.targetName ?? 'production run'}`}
        description="Backfill limits for this run's unlimited lines."
      />

      <Form {...form}>
        <form
          onSubmit={form.handleSubmit(onSubmit)}
          className="flex flex-1 flex-col gap-6"
        >
          <div className="flex flex-1 flex-col gap-4">
            {lines.length === 0 ? (
              <EmptyState
                title="No unlimited lines"
                description="Only unlimited (backfill) lines have limits to edit."
              />
            ) : (
              lines.map((line, index) => (
                <Card key={line.id}>
                  <CardHeader>
                    <CardTitle className="flex items-center gap-2 text-base">
                      {line.targetName}
                      <Badge variant="secondary">{line.targetKind}</Badge>
                      <span className="text-xs font-normal text-muted-foreground">
                        {line.cumulativeProducedUnits ?? 0} produced so far
                      </span>
                    </CardTitle>
                  </CardHeader>
                  <CardContent className="flex flex-wrap gap-4">
                    <FormField
                      control={form.control}
                      name={`lines.${index}.backfillPrinterCap`}
                      render={({ field }) => (
                        <FormItem>
                          <FormLabel>Backfill printer cap</FormLabel>
                          <FormControl>
                            <Input
                              type="number"
                              min={1}
                              step={1}
                              className="w-32"
                              {...field}
                            />
                          </FormControl>
                          <FormMessage />
                        </FormItem>
                      )}
                    />
                    <FormField
                      control={form.control}
                      name={`lines.${index}.backfillCumulativeUnitCeiling`}
                      render={({ field }) => (
                        <FormItem>
                          <FormLabel>Cumulative unit ceiling</FormLabel>
                          <FormControl>
                            <Input
                              type="number"
                              min={0}
                              step={1}
                              placeholder="No ceiling"
                              className="w-40"
                              {...field}
                            />
                          </FormControl>
                          <FormMessage />
                        </FormItem>
                      )}
                    />
                  </CardContent>
                </Card>
              ))
            )}
          </div>

          <FormFooter
            end={
              <>
                <Button
                  type="button"
                  variant="outline"
                  onClick={() => navigate(`/production-runs/${id}`)}
                >
                  Cancel
                </Button>
                <Button type="submit" disabled={!isDirty || pending}>
                  {pending && <Spinner data-icon="inline-start" />}
                  {pending ? 'Saving…' : 'Save'}
                </Button>
              </>
            }
          />
        </form>
      </Form>

      <ConfirmLeaveDialog />
    </Page>
  );
}
