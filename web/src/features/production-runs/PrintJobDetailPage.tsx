import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useFieldArray, useForm } from 'react-hook-form';
import { Link, useNavigate, useParams } from 'react-router';
import { toast } from 'sonner';
import { z } from 'zod';
import { BackLink } from '@/components/back-link/BackLink';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import {
  Dialog,
  DialogClose,
  DialogScrollableContent,
  DialogTitle,
  DialogTrigger,
} from '@/components/ui/dialog';
import { Field, FieldGroup, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { Skeleton } from '@/components/ui/skeleton';
import { Spinner } from '@/components/ui/spinner';
import { PartFilamentRequirementsList } from '@/features/parts/components/PartFilamentRequirementsList';
import { formatSlicer } from '@/features/slicing/formatSlicer';
import { apiUrl } from '@/lib/api/apiUrl';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import type { PrintJobResponse } from '@/lib/api/generated/endpoints.schemas';
import {
  useOpenFarmApiEndpointsPrintJobsCompletePrintJobEndpoint,
  useOpenFarmApiEndpointsPrintJobsFailPrintJobEndpoint,
  useOpenFarmApiEndpointsPrintJobsGetPrintJobEndpoint,
  useOpenFarmApiEndpointsPrintJobsMarkPrintJobStartedEndpoint,
  useOpenFarmApiEndpointsPrintJobsSetPrintJobFilamentRequirementsEndpoint,
} from '@/lib/api/generated/print-jobs/print-jobs';
import { useOpenFarmApiEndpointsPrintersListFleetEndpoint } from '@/lib/api/generated/printers/printers';
import { getToken } from '@/lib/auth/tokenStorage';
import { formatDateTime } from '@/lib/formatDateTime';
import { formatCost, formatGrams } from '@/lib/formatUnits';
import { JobMatchingDialog } from './components/JobMatchingDialog';
import { JobRemoteControls } from './components/JobRemoteControls';
import { RemoveJobButton } from './components/RemoveJobButton';
import { useProductionHub } from './hooks/useProductionHub';

const schema = z.object({
  lines: z
    .array(
      z.object({
        materialType: z.string(),
        color: z.string(),
        variantId: z.string().nullable().optional(),
        profileId: z.string().nullable().optional(),
      }),
    )
    .min(1),
});

const TERMINAL_STATUSES = ['Completed', 'Failed', 'Cancelled'];

/** Header-triggered dialog; the form mounts only while open so each open starts from the server state. */
function JobFilamentDialog({ job }: { job: PrintJobResponse }) {
  const [open, setOpen] = useState(false);
  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <Button variant="outline">Edit filament</Button>
      </DialogTrigger>
      {open && <JobFilamentForm job={job} onDone={() => setOpen(false)} />}
    </Dialog>
  );
}

function JobFilamentForm({
  job,
  onDone,
}: {
  job: PrintJobResponse;
  onDone: () => void;
}) {
  const queryClient = useQueryClient();
  const form = useForm<z.infer<typeof schema>>({
    resolver: zodResolver(schema),
    defaultValues: {
      lines: (job.filamentConsumption ?? []).map((c) => ({
        materialType: c.materialType ?? '',
        color: c.detectedColorHex ?? '',
        variantId: c.narrowFilamentVariantId ?? null,
        profileId: c.narrowFilamentProfileId ?? null,
      })),
    },
  });
  const fields = useFieldArray({ control: form.control, name: 'lines' });
  const requirements =
    useOpenFarmApiEndpointsPrintJobsSetPrintJobFilamentRequirementsEndpoint({
      mutation: {
        onSuccess: () => {
          queryClient.invalidateQueries();
          toast.success('Job filament saved.');
          onDone();
        },
        onError: () => toast.error('Could not save job filament.'),
      },
    });
  const saving = requirements.isPending;
  return (
    <DialogScrollableContent
      className="sm:max-w-3xl"
      header={<DialogTitle>Edit filament requirements</DialogTitle>}
      footer={
        <>
          <DialogClose asChild>
            <Button type="button" variant="outline">
              Cancel
            </Button>
          </DialogClose>
          <Button type="submit" form="job-filament-form" disabled={saving}>
            {saving && <Spinner data-icon="inline-start" />}
            {saving ? 'Saving…' : 'Save'}
          </Button>
        </>
      }
    >
      <form
        id="job-filament-form"
        onSubmit={form.handleSubmit(({ lines }) => {
          if (!job.id) return;
          requirements.mutate({
            id: job.id,
            data: {
              requirements: lines.map((line) => ({
                materialType: line.materialType || null,
                detectedColorHex: line.color || null,
                narrowFilamentVariantId: line.variantId ?? null,
                narrowFilamentProfileId: line.profileId ?? null,
              })),
            },
          });
        })}
      >
        <FieldGroup className="gap-4">
          <PartFilamentRequirementsList
            requirements={form.watch('lines').map((line, index) => ({
              key: fields.fields[index]?.id ?? String(index),
              materialType: line.materialType || null,
              detectedColorHex: line.color || null,
              detectedSubBrand: null,
              narrowFilamentVariantId: line.variantId,
              narrowFilamentProfileId: line.profileId,
            }))}
            onNarrow={(key, variantId) => {
              const index = fields.fields.findIndex(
                (field) => field.id === key,
              );
              if (index < 0) return;
              form.setValue(`lines.${index}.variantId`, variantId, {
                shouldDirty: true,
              });
              form.setValue(`lines.${index}.profileId`, null, {
                shouldDirty: true,
              });
            }}
            onNarrowToProfile={(key, profileId) => {
              const index = fields.fields.findIndex(
                (field) => field.id === key,
              );
              if (index < 0) return;
              form.setValue(`lines.${index}.profileId`, profileId, {
                shouldDirty: true,
              });
              form.setValue(`lines.${index}.variantId`, null, {
                shouldDirty: true,
              });
            }}
          />
          {fields.fields.map((field, index) => (
            <FieldGroup
              key={field.id}
              className="grid grid-cols-2 gap-3 md:grid-cols-4"
            >
              <Field>
                <FieldLabel htmlFor={`material-${index}`}>Material</FieldLabel>
                <Input
                  id={`material-${index}`}
                  placeholder="Any material"
                  {...form.register(`lines.${index}.materialType`)}
                />
              </Field>
              <Field>
                <FieldLabel htmlFor={`color-${index}`}>Color</FieldLabel>
                <Input
                  id={`color-${index}`}
                  placeholder="Any color"
                  {...form.register(`lines.${index}.color`)}
                />
              </Field>
              <Button
                type="button"
                variant="outline"
                onClick={() => fields.remove(index)}
              >
                Remove
              </Button>
            </FieldGroup>
          ))}
          {Object.keys(form.formState.errors).length > 0 && (
            <p role="alert">Enter valid filament lines.</p>
          )}
          <Button
            type="button"
            variant="outline"
            className="self-start"
            onClick={() => fields.append({ materialType: '', color: '' })}
          >
            Add requirement
          </Button>
        </FieldGroup>
      </form>
    </DialogScrollableContent>
  );
}

const PRICE_SOURCE_LABEL: Record<string, string> = {
  Spool: 'spool price',
  Variant: 'color price',
  Filament: 'filament price',
};

function JobFilamentSummary({ job }: { job: PrintJobResponse }) {
  const terminal = TERMINAL_STATUSES.includes(job.status ?? '');
  const lines = job.filamentConsumption ?? [];
  // The total adds the lines that have a cost. It is partial while any line has none: no price, or nothing deducted.
  const costs = lines.flatMap((line) => (line.cost != null ? [line.cost] : []));
  const totalCost = costs.reduce((sum, cost) => sum + cost, 0);
  const partial = costs.length < lines.length;
  return (
    <Card>
      <CardHeader>
        <CardTitle>
          {terminal ? 'Filament consumed' : 'Filament requirements'}
        </CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-2">
        {lines.length === 0 ? (
          <p className="text-sm text-muted-foreground">
            No filament requirements.
          </p>
        ) : (
          lines.map((line, index) => (
            <div
              key={line.id ?? index}
              className="flex items-center justify-between gap-2 rounded-md border p-2 text-sm"
            >
              <span className="flex items-center gap-2">
                {line.detectedColorHex && (
                  <span
                    aria-hidden
                    className="size-3 rounded-full border"
                    style={{
                      backgroundColor: `#${line.detectedColorHex.replace(/^#/, '')}`,
                    }}
                  />
                )}
                {line.materialType || 'Any material'}
                {line.detectedColorHex && (
                  <span className="text-muted-foreground">
                    #{line.detectedColorHex.replace(/^#/, '')}
                  </span>
                )}
              </span>
              {terminal ? (
                line.gramsConsumed != null ? (
                  <span className="flex flex-col items-end gap-0.5 text-right">
                    <span className="flex items-center gap-2 tabular-nums">
                      <span>{formatGrams(line.gramsConsumed)}</span>
                      {line.cost != null ? (
                        <span>{formatCost(line.cost)}</span>
                      ) : (
                        line.gramsConsumed > 0 && (
                          <span className="text-muted-foreground">
                            No price
                          </span>
                        )
                      )}
                    </span>
                    {(line.pricePerKg != null || line.spoolId) && (
                      <span className="flex items-center gap-2 text-xs text-muted-foreground">
                        {line.pricePerKg != null && (
                          <span>
                            {formatCost(line.pricePerKg)} per kg
                            {line.priceSource &&
                              ` · ${PRICE_SOURCE_LABEL[line.priceSource] ?? line.priceSource}`}
                          </span>
                        )}
                        {line.spoolId && (
                          <Link
                            to={`/spools/${line.spoolId}`}
                            className="underline-offset-4 hover:underline"
                          >
                            Spool
                          </Link>
                        )}
                      </span>
                    )}
                  </span>
                ) : (
                  <span className="text-muted-foreground">Not deducted</span>
                )
              ) : (
                line.estimatedGrams != null && (
                  <span className="text-muted-foreground">
                    Est. {formatGrams(line.estimatedGrams)}
                  </span>
                )
              )}
            </div>
          ))
        )}
        {terminal && costs.length > 0 && (
          <div className="flex items-center justify-between gap-2 px-2 pt-1 text-sm font-medium">
            <span className="flex items-center gap-2">
              Filament cost
              {partial && <Badge kind="amber">Partial</Badge>}
            </span>
            <span className="tabular-nums">{formatCost(totalCost)}</span>
          </div>
        )}
      </CardContent>
    </Card>
  );
}

/** Printer and times of a print the printer started itself — it has no run, part or queue slot to show. */
function ExternalPrintSummary({
  job,
  printerName,
}: {
  job: PrintJobResponse;
  printerName?: string;
}) {
  return (
    <Card>
      <CardHeader>
        <CardTitle>Print</CardTitle>
      </CardHeader>
      <CardContent>
        <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
          <dt className="text-muted-foreground">Printer</dt>
          <dd>
            {job.dispatchedPrinterId ? (
              <Link
                className="hover:underline"
                to={`/printers/${job.dispatchedPrinterId}`}
              >
                {printerName ?? 'Unknown printer'}
              </Link>
            ) : (
              'Printer removed'
            )}
          </dd>
          {job.startedAtUtc && (
            <>
              <dt className="text-muted-foreground">Started</dt>
              <dd>{formatDateTime(job.startedAtUtc)}</dd>
            </>
          )}
          {job.endedAtUtc && (
            <>
              <dt className="text-muted-foreground">Ended</dt>
              <dd>{formatDateTime(job.endedAtUtc)}</dd>
            </>
          )}
          {job.actualDurationMinutes != null && (
            <>
              <dt className="text-muted-foreground">Duration</dt>
              <dd>{job.actualDurationMinutes} min</dd>
            </>
          )}
        </dl>
      </CardContent>
    </Card>
  );
}

export function PrintJobDetailPage() {
  const { id = '' } = useParams();
  const navigate = useNavigate();
  useProductionHub();
  const queryClient = useQueryClient();
  const query = useOpenFarmApiEndpointsPrintJobsGetPrintJobEndpoint(id);
  const job = query.data?.data;
  const fleet =
    useOpenFarmApiEndpointsPrintersListFleetEndpoint().data?.data?.printers ??
    [];
  const printer = fleet.find((p) => p.id === job?.dispatchedPrinterId);
  const external = job?.source === 'External';
  const manual =
    !external && printer && !['Bambu', 'System'].includes(printer.brand ?? '');
  const options = {
    mutation: {
      onSuccess: () => queryClient.invalidateQueries(),
      onError: () => toast.error('Could not change job status.'),
    },
  };
  const started =
    useOpenFarmApiEndpointsPrintJobsMarkPrintJobStartedEndpoint(options);
  const complete =
    useOpenFarmApiEndpointsPrintJobsCompletePrintJobEndpoint(options);
  const fail = useOpenFarmApiEndpointsPrintJobsFailPrintJobEndpoint(options);
  async function download() {
    const token = getToken();
    const response = await fetch(apiUrl(`/api/v1/print-jobs/${id}/file`), {
      headers: { Authorization: `Bearer ${token}` },
    });
    if (!response.ok) {
      toast.error(
        await extractErrorDetail(
          response,
          'Could not retrieve job file. Assign a printer before slicing.',
        ),
      );
      return;
    }
    const url = URL.createObjectURL(await response.blob());
    const link = document.createElement('a');
    link.href = url;
    link.download =
      response.headers
        .get('content-disposition')
        ?.match(/filename="?([^";]+)/)?.[1] ?? 'print.gcode';
    link.click();
    URL.revokeObjectURL(url);
  }
  const backLink = (
    <BackLink to="/jobs" className="mb-1">
      Back to print queue
    </BackLink>
  );
  const queryError: unknown = query.error;
  const isNotFound =
    !query.isError ||
    (queryError instanceof Response && queryError.status === 404);
  return (
    <Page>
      {query.isLoading ? (
        <>
          <PageHeader above={backLink} title="Print job" />
          <Skeleton className="h-64 w-full" />
        </>
      ) : !job ? (
        <>
          <PageHeader above={backLink} title="Print job" />
          {isNotFound ? (
            <EmptyState
              title="Print job not found"
              description="Removed or invalid link."
              action={
                <Button variant="outline" onClick={() => navigate('/jobs')}>
                  Back to print queue
                </Button>
              }
            />
          ) : (
            <ErrorState
              title="Could not load print job"
              onRetry={() => query.refetch()}
            />
          )}
        </>
      ) : (
        <>
          <PageHeader
            above={backLink}
            title={
              <span className="flex flex-wrap items-center gap-3">
                {external ? job.name : `Print job · ${job.plateQuantity} units`}
                <Badge>{job.status}</Badge>
              </span>
            }
            description={
              external
                ? 'Started on the printer, not from OpenFarm'
                : `File revision ${job.fileRevisionNumber} · ${job.fileRevisionHash}`
            }
            action={
              external ? undefined : (
                <div className="flex flex-wrap items-center gap-2">
                  {manual &&
                    job.status === 'Queued' &&
                    job.dispatchedPrinterId && (
                      <Button onClick={() => started.mutate({ id })}>
                        Mark started
                      </Button>
                    )}
                  {manual &&
                    ['Printing', 'Paused'].includes(job.status ?? '') && (
                      <>
                        <Button onClick={() => complete.mutate({ id })}>
                          Mark completed
                        </Button>
                        <Button
                          variant="outline"
                          onClick={() => fail.mutate({ id })}
                        >
                          Mark failed
                        </Button>
                      </>
                    )}
                  {job.status === 'Queued' && !job.dispatchedAtUtc && (
                    <JobFilamentDialog job={job} />
                  )}
                  <JobMatchingDialog
                    jobId={id}
                    trigger={<Button variant="outline">Job diagnosis</Button>}
                  />
                  <Button
                    variant="outline"
                    onClick={download}
                    disabled={job.snapshotState !== 'Ready'}
                    aria-describedby={
                      job.snapshotState !== 'Ready'
                        ? 'job-source-warning'
                        : undefined
                    }
                    title={
                      job.provenance
                        ? 'The file is made again with the current settings. It can differ from the sent file.'
                        : undefined
                    }
                  >
                    Download print file
                  </Button>
                  {job.status === 'Queued' && !job.dispatchedAtUtc && (
                    <RemoveJobButton jobId={id} />
                  )}
                </div>
              )
            }
          />
          {external ? (
            <ExternalPrintSummary job={job} printerName={printer?.name} />
          ) : (
            <JobRemoteControls jobId={id} status={job.status ?? ''} />
          )}
          {!external && job.snapshotState !== 'Ready' && (
            <Alert id="job-source-warning">
              <AlertTitle>
                {job.snapshotState === 'Unavailable'
                  ? 'Job source removed'
                  : 'Job snapshot needs recovery'}
              </AlertTitle>
              <AlertDescription>
                {job.snapshotRecoveryReason ??
                  'The original production inputs could not be verified. Restore the original inputs or cancel and recreate the run.'}
              </AlertDescription>
            </Alert>
          )}
          {!external && job.status === 'Queued' && job.lastBlockReason && (
            <Alert variant="destructive" id="job-last-attempt">
              <AlertTitle>Last attempt failed</AlertTitle>
              <AlertDescription>{job.lastBlockReason}</AlertDescription>
            </Alert>
          )}
          {!external && job.provenance && (
            <Alert id="job-provenance">
              <AlertTitle>
                Sliced with{' '}
                {formatSlicer(job.provenance.engine, job.provenance.version)}
              </AlertTitle>
              <AlertDescription>
                <ul>
                  {(job.provenance.presets ?? []).map((preset) => (
                    <li key={`${preset.kind}:${preset.resolvedName}`}>
                      {preset.kind}: {preset.resolvedName}
                      {preset.selectedName !== preset.resolvedName
                        ? ` (selected: ${preset.selectedName})`
                        : ''}
                      {preset.outcome === 'MappedEngine'
                        ? ' — the slicer’s own copy of the selected profile'
                        : ''}
                    </li>
                  ))}
                </ul>
              </AlertDescription>
            </Alert>
          )}
          <JobFilamentSummary job={job} />
        </>
      )}
    </Page>
  );
}
