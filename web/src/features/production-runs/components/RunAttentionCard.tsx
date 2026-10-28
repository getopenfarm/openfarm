import { useQueryClient } from '@tanstack/react-query';
import { AlertTriangleIcon } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router';
import { toast } from 'sonner';
import { ConfirmDialog } from '@/components/confirm-dialog/ConfirmDialog';
import { Button } from '@/components/ui/button';
import {
  Card,
  CardAction,
  CardContent,
  CardHeader,
  CardTitle,
} from '@/components/ui/card';
import { stripConfigurationLink } from '@/features/slicing/stripConfigurationLink';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import type {
  ProductionRunDetailResponse,
  RunJobSummaryResponse,
} from '@/lib/api/generated/endpoints.schemas';
import {
  useOpenFarmApiEndpointsPrintJobsRequeuePrintJobEndpoint,
  useOpenFarmApiEndpointsPrintJobsWriteOffPrintJobEndpoint,
} from '@/lib/api/generated/print-jobs/print-jobs';
import { useOpenFarmApiEndpointsProductionRunsRequeueFailedJobsEndpoint } from '@/lib/api/generated/production-runs/production-runs';
import { formatShortDateTime } from '@/lib/formatDateTime';
import { pluralize } from '@/lib/pluralize';
import { invalidateProductionQueries } from '../hooks/useProductionHub';
import { JobMatchingDialog } from './JobMatchingDialog';

interface Lost {
  job: RunJobSummaryResponse;
  partName: string;
  plate: number;
  unitsPerPlate: number;
}

/**
 * Everything on the run that waits for the operator, in one place: lost plates (requeue or write off), parts no
 * printer can take, and sends that failed. Renders nothing when the run needs nothing.
 */
export function RunAttentionCard({
  run,
  printerName,
}: {
  run: ProductionRunDetailResponse;
  printerName: (id?: string | null) => string | undefined;
}) {
  const queryClient = useQueryClient();
  const [writeOff, setWriteOff] = useState<Lost | null>(null);
  const requirements = (run.lines ?? []).flatMap((l) => l.requirements ?? []);
  const lost: Lost[] = requirements.flatMap((r) =>
    (r.jobs ?? []).flatMap((job, index) =>
      job.needsAction
        ? [
            {
              job,
              partName: r.partName ?? 'Part',
              plate: index + 1,
              unitsPerPlate: r.unitsPerPlate ?? 1,
            },
          ]
        : [],
    ),
  );
  // Parts waiting for the same reason share one row: the fix is the same.
  const byReason = new Map<string, typeof requirements>();
  for (const r of requirements.filter((r) => r.blockingReason)) {
    const reason = stripConfigurationLink(r.blockingReason ?? '');
    byReason.set(reason, [...(byReason.get(reason) ?? []), r]);
  }
  const blocked = [...byReason];
  const sendFailed = requirements.flatMap((r) =>
    (r.jobs ?? []).flatMap((job, index) =>
      job.lastBlockReason
        ? [{ job, partName: r.partName ?? 'Part', plate: index + 1 }]
        : [],
    ),
  );
  const requeueable = lost.filter((l) => l.job.canRequeue).length;

  const done = (message: string) => () => {
    toast.success(message);
    invalidateProductionQueries(queryClient);
  };
  const failed = (fallback: string) => async (error: unknown) => {
    toast.error(await extractErrorDetail(error, fallback));
  };
  const requeue = useOpenFarmApiEndpointsPrintJobsRequeuePrintJobEndpoint({
    mutation: {
      onSuccess: done('Plate requeued.'),
      onError: failed('Could not requeue plate.'),
    },
  });
  const writeOffMutation =
    useOpenFarmApiEndpointsPrintJobsWriteOffPrintJobEndpoint({
      mutation: {
        onSuccess: done('Plate written off.'),
        onError: failed('Could not write off plate.'),
      },
    });
  const requeueAll =
    useOpenFarmApiEndpointsProductionRunsRequeueFailedJobsEndpoint({
      mutation: {
        onSuccess: done('Lost plates requeued.'),
        onError: failed('Could not requeue plates.'),
      },
    });
  const pending =
    requeue.isPending || writeOffMutation.isPending || requeueAll.isPending;

  if (lost.length === 0 && blocked.length === 0 && sendFailed.length === 0)
    return null;

  return (
    <Card className="gap-3 border-destructive/40">
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <AlertTriangleIcon className="size-4 text-destructive" />
          Needs attention
        </CardTitle>
        {requeueable > 1 && (
          <CardAction>
            <Button
              size="sm"
              variant="outline"
              disabled={pending}
              onClick={() => requeueAll.mutate({ id: run.id ?? '' })}
            >
              Requeue all {requeueable}
            </Button>
          </CardAction>
        )}
      </CardHeader>
      <CardContent>
        <ul className="flex flex-col divide-y">
          {lost.map(({ job, partName, plate, unitsPerPlate }) => (
            <li
              key={job.id}
              className="flex flex-wrap items-center gap-x-3 gap-y-1 py-2 first:pt-0 last:pb-0"
            >
              <span className="min-w-0 flex-1 text-sm">
                <span className="font-medium">{partName}</span>
                {' · '}
                <Link to={`/jobs/${job.id}`} className="hover:underline">
                  plate {plate}
                </Link>{' '}
                <span className="text-destructive">
                  {job.status === 'Failed' ? 'failed' : 'cancelled'}
                </span>
                <span className="text-muted-foreground">
                  {printerName(job.printerId) &&
                    ` on ${printerName(job.printerId)}`}
                  {job.endedAtUtc &&
                    ` · ${formatShortDateTime(job.endedAtUtc)}`}
                  {!job.canRequeue && ' · original inputs unavailable'}
                </span>
              </span>
              <div className="flex gap-2">
                {job.canRequeue && (
                  <Button
                    size="sm"
                    disabled={pending}
                    onClick={() => requeue.mutate({ id: job.id ?? '' })}
                  >
                    Requeue
                  </Button>
                )}
                <Button
                  size="sm"
                  variant="outline"
                  disabled={pending}
                  onClick={() =>
                    setWriteOff({ job, partName, plate, unitsPerPlate })
                  }
                >
                  Write off
                </Button>
              </div>
            </li>
          ))}
          {blocked.map(([reason, waiting]) => {
            const next = waiting
              .flatMap((r) => r.jobs ?? [])
              .find((j) => j.status === 'Queued' && !j.lastBlockReason);
            return (
              <li
                key={reason}
                className="flex flex-wrap items-center gap-x-3 gap-y-1 py-2 first:pt-0 last:pb-0"
              >
                <span className="min-w-0 flex-1 text-sm">
                  <span className="font-medium">
                    {waiting.map((r) => r.partName).join(', ')}
                  </span>
                  {' · '}
                  <span className="text-status-attention">{reason}</span>
                </span>
                {next?.id && (
                  <JobMatchingDialog
                    jobId={next.id}
                    trigger={
                      <Button size="sm" variant="outline">
                        Diagnose
                      </Button>
                    }
                  />
                )}
              </li>
            );
          })}
          {sendFailed.map(({ job, partName, plate }) => (
            <li
              key={`send-${job.id}`}
              className="flex flex-wrap items-center gap-x-3 gap-y-1 py-2 first:pt-0 last:pb-0"
            >
              <span className="min-w-0 flex-1 text-sm">
                <span className="font-medium">{partName}</span>
                {' · '}
                <Link to={`/jobs/${job.id}`} className="hover:underline">
                  plate {plate}
                </Link>{' '}
                <span className="text-status-attention">
                  send failed:{' '}
                  {stripConfigurationLink(job.lastBlockReason ?? '')}
                </span>
              </span>
            </li>
          ))}
        </ul>
      </CardContent>
      <ConfirmDialog
        open={writeOff !== null}
        onOpenChange={(open) => {
          if (!open) setWriteOff(null);
        }}
        title={`Write off ${writeOff?.partName ?? ''} plate ${writeOff?.plate ?? ''}?`}
        description={`Not reprinted: the run can complete without its ${pluralize(writeOff?.unitsPerPlate ?? 1, 'unit')}.`}
        confirmLabel="Write off"
        destructive
        isPending={writeOffMutation.isPending}
        onConfirm={() => {
          if (writeOff?.job.id)
            writeOffMutation.mutate({ id: writeOff.job.id });
          setWriteOff(null);
        }}
      />
    </Card>
  );
}
