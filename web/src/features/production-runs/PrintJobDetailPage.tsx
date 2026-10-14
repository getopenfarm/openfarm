import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Controller, useFieldArray, useForm } from 'react-hook-form';
import { useNavigate, useParams } from 'react-router';
import { toast } from 'sonner';
import { z } from 'zod';
import { BackLink } from '@/components/back-link/BackLink';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
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
import {
  Field,
  FieldError,
  FieldGroup,
  FieldLabel,
} from '@/components/ui/field';
import { Input } from '@/components/ui/input';
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
import { PartFilamentRequirementsList } from '@/features/parts/components/PartFilamentRequirementsList';
import type { PrintJobResponse } from '@/lib/api/generated/endpoints.schemas';
import {
  useOpenFarmApiEndpointsPrintJobsCompletePrintJobEndpoint,
  useOpenFarmApiEndpointsPrintJobsFailPrintJobEndpoint,
  useOpenFarmApiEndpointsPrintJobsGetPrintJobEndpoint,
  useOpenFarmApiEndpointsPrintJobsMarkPrintJobStartedEndpoint,
  useOpenFarmApiEndpointsPrintJobsRecordPrintJobFilamentConsumptionEndpoint,
  useOpenFarmApiEndpointsPrintJobsSetPrintJobFilamentRequirementsEndpoint,
} from '@/lib/api/generated/print-jobs/print-jobs';
import { useOpenFarmApiEndpointsPrintersListFleetEndpoint } from '@/lib/api/generated/printers/printers';
import { useOpenFarmApiEndpointsSpoolsListSpoolsEndpoint } from '@/lib/api/generated/spools/spools';
import { getToken } from '@/lib/auth/tokenStorage';
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
        spoolId: z.string(),
        grams: z.number().min(0),
        variantId: z.string().nullable().optional(),
        profileId: z.string().nullable().optional(),
      }),
    )
    .min(1),
});

const TERMINAL_STATUSES = ['Completed', 'Failed', 'Cancelled'];

type FilamentDialogMode = 'requirements' | 'usage';

/** Header-triggered dialog; the form mounts only while open so each open starts from the server state. */
function JobFilamentDialog({
  job,
  mode,
}: {
  job: PrintJobResponse;
  mode: FilamentDialogMode;
}) {
  const [open, setOpen] = useState(false);
  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <Button variant="outline">
          {mode === 'requirements' ? 'Edit filament' : 'Record usage'}
        </Button>
      </DialogTrigger>
      {open && (
        <JobFilamentForm job={job} mode={mode} onDone={() => setOpen(false)} />
      )}
    </Dialog>
  );
}

function JobFilamentForm({
  job,
  mode,
  onDone,
}: {
  job: PrintJobResponse;
  mode: FilamentDialogMode;
  onDone: () => void;
}) {
  const queryClient = useQueryClient();
  const editable = mode === 'requirements';
  const terminal = mode === 'usage';
  const spools =
    useOpenFarmApiEndpointsSpoolsListSpoolsEndpoint().data?.data?.spools ?? [];
  const form = useForm<z.infer<typeof schema>>({
    resolver: zodResolver(schema),
    defaultValues: {
      lines: (job.filamentConsumption ?? []).map((c) => ({
        materialType: c.materialType ?? '',
        color: c.detectedColorHex ?? '',
        spoolId: c.spoolId ?? '',
        grams: c.gramsConsumed ?? 0,
        variantId: c.narrowFilamentVariantId ?? null,
        profileId: c.narrowFilamentProfileId ?? null,
      })),
    },
  });
  const fields = useFieldArray({ control: form.control, name: 'lines' });
  const options = {
    mutation: {
      onSuccess: () => {
        queryClient.invalidateQueries();
        toast.success(editable ? 'Saved.' : 'Usage recorded.');
        onDone();
      },
      onError: () => toast.error('Could not save job filament.'),
    },
  };
  const requirements =
    useOpenFarmApiEndpointsPrintJobsSetPrintJobFilamentRequirementsEndpoint(
      options,
    );
  const usage =
    useOpenFarmApiEndpointsPrintJobsRecordPrintJobFilamentConsumptionEndpoint(
      options,
    );
  const saving = requirements.isPending || usage.isPending;
  return (
    <DialogScrollableContent
      className="sm:max-w-3xl"
      header={
        <DialogTitle>
          {editable ? 'Edit filament requirements' : 'Record filament usage'}
        </DialogTitle>
      }
      footer={
        <>
          <DialogClose asChild>
            <Button type="button" variant="outline">
              Cancel
            </Button>
          </DialogClose>
          <Button type="submit" form="job-filament-form" disabled={saving}>
            {saving && <Spinner data-icon="inline-start" />}
            {editable
              ? saving
                ? 'Saving…'
                : 'Save'
              : saving
                ? 'Recording…'
                : 'Record usage'}
          </Button>
        </>
      }
    >
      <form
        id="job-filament-form"
        onSubmit={form.handleSubmit(({ lines }) => {
          const values = lines.map((line) => ({
            materialType: line.materialType || null,
            detectedColorHex: line.color || null,
            narrowFilamentVariantId: line.variantId ?? null,
            narrowFilamentProfileId: line.profileId ?? null,
            spoolId: line.spoolId || null,
            gramsConsumed: line.grams,
          }));
          if (!job.id) return;
          if (editable)
            requirements.mutate({ id: job.id, data: { requirements: values } });
          else
            usage.mutate({ id: job.id, data: { filamentConsumption: values } });
        })}
      >
        <FieldGroup className="gap-4">
          {editable && (
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
          )}
          {fields.fields.map((field, index) => (
            <FieldGroup
              key={field.id}
              className="grid grid-cols-2 gap-3 md:grid-cols-4"
            >
              <Field data-disabled={!editable}>
                <FieldLabel htmlFor={`material-${index}`}>Material</FieldLabel>
                <Input
                  id={`material-${index}`}
                  placeholder="Any material"
                  disabled={!editable}
                  {...form.register(`lines.${index}.materialType`)}
                />
              </Field>
              <Field data-disabled={!editable}>
                <FieldLabel htmlFor={`color-${index}`}>Color</FieldLabel>
                <Input
                  id={`color-${index}`}
                  placeholder="Any color"
                  disabled={!editable}
                  {...form.register(`lines.${index}.color`)}
                />
              </Field>
              {terminal && (
                <>
                  <Field>
                    <FieldLabel htmlFor={`spool-${index}`}>Spool</FieldLabel>
                    <Controller
                      control={form.control}
                      name={`lines.${index}.spoolId`}
                      render={({ field }) => (
                        <Select
                          name={field.name}
                          value={field.value || '__untracked'}
                          onValueChange={(value) =>
                            field.onChange(value === '__untracked' ? '' : value)
                          }
                        >
                          <SelectTrigger
                            id={`spool-${index}`}
                            ref={field.ref}
                            onBlur={field.onBlur}
                            className="w-full"
                          >
                            <SelectValue />
                          </SelectTrigger>
                          <SelectContent>
                            <SelectGroup>
                              <SelectItem value="__untracked">
                                Untracked spool
                              </SelectItem>
                              {spools
                                .filter((spool) => spool.id)
                                .map((spool) => (
                                  <SelectItem
                                    key={spool.id}
                                    value={spool.id ?? ''}
                                  >
                                    {spool.label ?? spool.id}
                                  </SelectItem>
                                ))}
                            </SelectGroup>
                          </SelectContent>
                        </Select>
                      )}
                    />
                  </Field>
                  <Field
                    data-invalid={!!form.formState.errors.lines?.[index]?.grams}
                  >
                    <FieldLabel htmlFor={`grams-${index}`}>
                      Grams consumed
                    </FieldLabel>
                    <Input
                      id={`grams-${index}`}
                      aria-invalid={
                        !!form.formState.errors.lines?.[index]?.grams
                      }
                      aria-describedby={
                        form.formState.errors.lines?.[index]?.grams
                          ? `grams-${index}-error`
                          : undefined
                      }
                      type="number"
                      min={0}
                      step="0.1"
                      {...form.register(`lines.${index}.grams`, {
                        valueAsNumber: true,
                      })}
                    />
                    <FieldError
                      id={`grams-${index}-error`}
                      errors={[form.formState.errors.lines?.[index]?.grams]}
                    />
                  </Field>
                </>
              )}
              {terminal && (
                <Button
                  type="button"
                  variant="outline"
                  onClick={() =>
                    fields.append({
                      ...form.getValues(`lines.${index}`),
                      spoolId: '',
                      grams: 0,
                    })
                  }
                >
                  Add spool for {field.materialType || 'this material'}
                </Button>
              )}
              {editable && (
                <Button
                  type="button"
                  variant="outline"
                  onClick={() => fields.remove(index)}
                >
                  Remove
                </Button>
              )}
            </FieldGroup>
          ))}
          {Object.keys(form.formState.errors).length > 0 && (
            <p role="alert">
              Enter valid filament lines and non-negative grams.
            </p>
          )}
          {editable && (
            <Button
              type="button"
              variant="outline"
              className="self-start"
              onClick={() =>
                fields.append({
                  materialType: '',
                  color: '',
                  spoolId: '',
                  grams: 0,
                })
              }
            >
              Add requirement
            </Button>
          )}
        </FieldGroup>
      </form>
    </DialogScrollableContent>
  );
}

function JobFilamentSummary({ job }: { job: PrintJobResponse }) {
  const terminal = TERMINAL_STATUSES.includes(job.status ?? '');
  const lines = job.filamentConsumption ?? [];
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
              {terminal && (
                <span className="text-muted-foreground">
                  {line.gramsConsumed != null
                    ? `${line.gramsConsumed} g`
                    : 'Not recorded'}
                </span>
              )}
            </div>
          ))
        )}
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
  const manual = printer && !['Bambu', 'System'].includes(printer.brand ?? '');
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
    const response = await fetch(
      `${import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:8888'}/api/v1/print-jobs/${id}/file`,
      { headers: { Authorization: `Bearer ${token}` } },
    );
    if (!response.ok) {
      toast.error(
        'Could not retrieve the job file. Assign a printer before slicing.',
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
    <div className="flex flex-col gap-6">
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
              description="It may have been removed, or the link is out of date."
              action={
                <Button variant="outline" onClick={() => navigate('/jobs')}>
                  Back to print queue
                </Button>
              }
            />
          ) : (
            <ErrorState
              title="Could not load this print job"
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
                Print job · {job.plateQuantity} units
                <Badge>{job.status}</Badge>
              </span>
            }
            description={`File revision ${job.fileRevisionNumber} · ${job.fileRevisionHash}`}
            action={
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
                      <Button
                        onClick={() =>
                          complete.mutate({
                            id,
                            data: { filamentConsumption: [] },
                          })
                        }
                      >
                        Mark completed
                      </Button>
                      <Button
                        variant="outline"
                        onClick={() =>
                          fail.mutate({ id, data: { filamentConsumption: [] } })
                        }
                      >
                        Mark failed
                      </Button>
                    </>
                  )}
                {job.status === 'Queued' && !job.dispatchedAtUtc && (
                  <JobFilamentDialog job={job} mode="requirements" />
                )}
                {TERMINAL_STATUSES.includes(job.status ?? '') && (
                  <JobFilamentDialog job={job} mode="usage" />
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
                >
                  Download print file
                </Button>
                {job.status === 'Queued' && !job.dispatchedAtUtc && (
                  <RemoveJobButton jobId={id} />
                )}
              </div>
            }
          />
          <JobRemoteControls jobId={id} status={job.status ?? ''} />
          {job.snapshotState !== 'Ready' && (
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
          <JobFilamentSummary job={job} />
        </>
      )}
    </div>
  );
}
