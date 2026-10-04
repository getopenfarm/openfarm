import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { useFieldArray, useForm } from 'react-hook-form';
import { useParams } from 'react-router';
import { toast } from 'sonner';
import { z } from 'zod';
import { BackLink } from '@/components/back-link/BackLink';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
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
import { JobRemoteControls } from './components/JobRemoteControls';
import { useProductionHub } from './hooks/useProductionHub';

const schema = z.object({
  lines: z
    .array(
      z.object({
        materialType: z.string(),
        color: z.string(),
        spoolId: z.string(),
        grams: z.number().min(0),
      }),
    )
    .min(1),
});

function JobFilamentForm({ job }: { job: PrintJobResponse }) {
  const queryClient = useQueryClient();
  const editable = job.status === 'Queued' && !job.dispatchedAtUtc;
  const terminal = ['Completed', 'Failed', 'Cancelled'].includes(
    job.status ?? '',
  );
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
      })),
    },
  });
  const fields = useFieldArray({ control: form.control, name: 'lines' });
  const options = {
    mutation: {
      onSuccess: () => {
        queryClient.invalidateQueries();
        toast.success('Job filament saved.');
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
  return (
    <form
      className="space-y-3 rounded-md border p-4"
      onSubmit={form.handleSubmit(({ lines }) => {
        const values = lines.map((line) => ({
          materialType: line.materialType || null,
          detectedColorHex: line.color || null,
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
      <h3 className="font-semibold">
        {terminal ? 'Filament consumed' : 'Filament requirements'}
      </h3>
      {fields.fields.map((field, index) => (
        <div key={field.id} className="grid grid-cols-2 gap-3 md:grid-cols-4">
          <div>
            <Label htmlFor={`material-${index}`}>Material</Label>
            <Input
              id={`material-${index}`}
              placeholder="Any material"
              disabled={!editable}
              {...form.register(`lines.${index}.materialType`)}
            />
          </div>
          <div>
            <Label htmlFor={`color-${index}`}>Color</Label>
            <Input
              id={`color-${index}`}
              placeholder="Any color"
              disabled={!editable}
              {...form.register(`lines.${index}.color`)}
            />
          </div>
          {terminal && (
            <>
              <div>
                <Label htmlFor={`spool-${index}`}>Spool</Label>
                <select
                  id={`spool-${index}`}
                  className="h-9 w-full rounded-md border bg-background px-2"
                  {...form.register(`lines.${index}.spoolId`)}
                >
                  <option value="">Untracked spool</option>
                  {spools.map((s) => (
                    <option key={s.id} value={s.id}>
                      {s.label ?? s.id}
                    </option>
                  ))}
                </select>
              </div>
              <div>
                <Label htmlFor={`grams-${index}`}>Grams consumed</Label>
                <Input
                  id={`grams-${index}`}
                  type="number"
                  min={0}
                  step="0.1"
                  {...form.register(`lines.${index}.grams`, {
                    valueAsNumber: true,
                  })}
                />
              </div>
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
        </div>
      ))}
      {Object.keys(form.formState.errors).length > 0 && (
        <p role="alert">Enter valid filament lines and non-negative grams.</p>
      )}
      {(editable || terminal) && (
        <div className="flex gap-2">
          {editable && (
            <Button
              type="button"
              variant="outline"
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
          <Button
            type="submit"
            disabled={requirements.isPending || usage.isPending}
          >
            Save filament
          </Button>
        </div>
      )}
    </form>
  );
}

export function PrintJobDetailPage() {
  const { id = '' } = useParams();
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
  return (
    <div className="flex flex-col gap-6">
      <BackLink to="/jobs">Back to print queue</BackLink>
      {query.isLoading ? (
        <p>Loading job…</p>
      ) : !job ? (
        <p>Print job not found.</p>
      ) : (
        <>
          <div className="flex items-center gap-3">
            <h2 className="text-xl font-semibold">
              Print job · {job.plateQuantity} units
            </h2>
            <Badge>{job.status}</Badge>
            <Button variant="outline" onClick={download}>
              Download print file
            </Button>
          </div>
          <p className="text-sm text-muted-foreground">
            File revision {job.fileRevisionNumber} · {job.fileRevisionHash}
          </p>
          <JobRemoteControls jobId={id} status={job.status ?? ''} />
          {manual && (
            <div className="flex gap-2">
              {job.status === 'Queued' && job.dispatchedPrinterId && (
                <Button onClick={() => started.mutate({ id })}>
                  Mark started
                </Button>
              )}
              {['Printing', 'Paused'].includes(job.status ?? '') && (
                <>
                  <Button
                    onClick={() =>
                      complete.mutate({ id, data: { filamentConsumption: [] } })
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
            </div>
          )}
          <JobFilamentForm
            key={`${job.id}-${job.status}-${JSON.stringify(job.filamentConsumption)}`}
            job={job}
          />
        </>
      )}
    </div>
  );
}
