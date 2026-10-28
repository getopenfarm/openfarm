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
  useOpenFarmApiEndpointsProductionRunsGetProductionRunEndpoint,
  useOpenFarmApiEndpointsProductionRunsUpdateProductionRunEndpoint,
} from '@/lib/api/generated/production-runs/production-runs';
import {
  RunDetailsFields,
  runDetailsRequest,
  runDetailsSchema,
} from './components/RunDetailsFields';
import { invalidateProductionQueries } from './hooks/useProductionHub';
import { isOpenRun } from './lib/runStatus';

const wholeNumber = (min: number, message: string) =>
  z
    .string()
    .refine((v) => Number.isInteger(Number(v)) && Number(v) >= min, message);

const schema = runDetailsSchema.extend({
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
            v.trim() === '' || (Number.isInteger(Number(v)) && Number(v) >= 1),
          'Must be 1 or more, or blank for no ceiling.',
        ),
    }),
  ),
});

type FormValues = z.infer<typeof schema>;

const unlimitedLines = (run: ProductionRunDetailResponse | undefined) =>
  (run?.lines ?? []).filter((l) => l.quantity == null && l.id);

function toFormValues(run: ProductionRunDetailResponse): FormValues {
  return {
    name: run.name ?? '',
    dueDate: run.dueDate ?? '',
    notes: run.notes ?? '',
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

/** An open run's details and its unlimited lines' backfill limits, as one draft saved in one call. Lines are added from the run page. */
export function ProductionRunEditPage() {
  const { id = '' } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const runQuery =
    useOpenFarmApiEndpointsProductionRunsGetProductionRunEndpoint(id);
  const run = runQuery.data?.data as ProductionRunDetailResponse | undefined;

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { name: '', dueDate: '', notes: '', lines: [] },
  });
  const { isDirty } = form.formState;

  // biome-ignore lint/correctness/useExhaustiveDependencies: seed once per run, not on every hub-driven refetch (which would clobber the draft).
  useEffect(() => {
    if (run?.id) form.reset(toFormValues(run));
  }, [run?.id]);

  const { ConfirmLeaveDialog, markClean } = useUnsavedChangesPrompt(isDirty);

  const mutation =
    useOpenFarmApiEndpointsProductionRunsUpdateProductionRunEndpoint({
      mutation: {
        onSuccess: () => {
          toast.success('Run saved.');
          invalidateProductionQueries(queryClient);
          markClean();
          navigate(`/production-runs/${id}`);
        },
        onError: async (error: unknown) => {
          toast.error(await extractErrorDetail(error, 'Could not save run.'));
        },
      },
    });

  const backLink = (
    <BackLink to={`/production-runs/${id}`} className="mb-1">
      Back to run
    </BackLink>
  );

  if (!run?.id || !isOpenRun(run.status)) {
    return (
      <Page>
        <PageHeader above={backLink} title="Edit run" />
        {runQuery.isLoading ? (
          <Skeleton className="h-64 w-full" />
        ) : (
          <EmptyState
            title={run?.id ? 'Run is closed' : 'Run not found'}
            description={
              run?.id
                ? 'Closed and cancelled runs keep their record as is.'
                : 'Removed or invalid link.'
            }
            action={
              <Button
                variant="outline"
                onClick={() => navigate('/production-runs')}
              >
                Back to runs
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
    mutation.mutate({
      id,
      data: {
        ...runDetailsRequest(values),
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
      <PageHeader above={backLink} title={`Edit ${run.displayLabel}`} />

      <Form {...form}>
        <form
          onSubmit={form.handleSubmit(onSubmit)}
          className="flex flex-1 flex-col gap-6"
        >
          <div className="flex max-w-3xl flex-1 flex-col gap-6">
            <RunDetailsFields control={form.control} />
            {lines.length > 0 && (
              <Card>
                <CardHeader>
                  <CardTitle>Backfill limits</CardTitle>
                </CardHeader>
                <CardContent className="flex flex-col divide-y">
                  {lines.map((line, index) => (
                    <div
                      key={line.id}
                      className="grid gap-4 py-3 first:pt-0 last:pb-0 sm:grid-cols-[minmax(0,1fr)_9rem_9rem] sm:items-start"
                    >
                      <div className="min-w-0 pt-1">
                        <span className="block truncate font-medium">
                          {line.targetName}
                        </span>
                        <span className="text-xs text-muted-foreground">
                          {line.cumulativeProducedUnits ?? 0} produced so far
                        </span>
                      </div>
                      <FormField
                        control={form.control}
                        name={`lines.${index}.backfillPrinterCap`}
                        render={({ field }) => (
                          <FormItem>
                            <FormLabel>Printers at once</FormLabel>
                            <FormControl>
                              <Input
                                type="number"
                                min={1}
                                step={1}
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
                            <FormLabel>Stop at units</FormLabel>
                            <FormControl>
                              <Input
                                type="number"
                                min={1}
                                step={1}
                                placeholder="No ceiling"
                                {...field}
                              />
                            </FormControl>
                            <FormMessage />
                          </FormItem>
                        )}
                      />
                    </div>
                  ))}
                </CardContent>
              </Card>
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
