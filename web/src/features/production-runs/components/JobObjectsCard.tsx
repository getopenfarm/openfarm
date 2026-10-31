import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { ConfirmDialog } from '@/components/confirm-dialog/ConfirmDialog';
import { ErrorState } from '@/components/empty-state/EmptyState';
import { Alert, AlertDescription } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Checkbox } from '@/components/ui/checkbox';
import { Field, FieldGroup, FieldLabel } from '@/components/ui/field';
import { Skeleton } from '@/components/ui/skeleton';
import { PrintObjectOutlines } from '@/features/parts/components/PrintObjectOutlines';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import type { PrintObjectExecution } from '@/lib/api/generated/endpoints.schemas';
import {
  openFarmApiEndpointsPrintJobsConfirmPrintNotStartedEndpoint as confirmNotStarted,
  openFarmApiEndpointsPrintJobsConfirmObjectOutputEndpoint as confirmOutput,
  openFarmApiEndpointsPrintJobsGetJobObjectsEndpoint as read,
  openFarmApiEndpointsPrintJobsRequeuePrintJobEndpoint as requeue,
  openFarmApiEndpointsPrintJobsSkipJobObjectsEndpoint as skip,
  openFarmApiEndpointsPrintJobsWriteOffPrintJobEndpoint as writeOff,
} from '@/lib/api/generated/print-jobs/print-jobs';
import { invalidateProductionQueries } from '../hooks/useProductionHub';

export function JobObjectsCard({
  jobId,
  status,
  produced,
  target,
  canResolve = false,
  canRequeue = true,
}: {
  jobId: string;
  status: string;
  produced?: number | null;
  target?: number | null;
  canResolve?: boolean;
  canRequeue?: boolean;
}) {
  const client = useQueryClient();
  const live = status === 'Printing' || status === 'Paused';
  const query = useQuery({
    queryKey: ['/api/v1/print-jobs', jobId, 'objects', status],
    queryFn: async () =>
      (await read(jobId)).data as PrintObjectExecution | null,
  });
  const [selected, setSelected] = useState<string[]>([]);
  useEffect(() => {
    if (status) setSelected([]);
  }, [status]);
  const [confirm, setConfirm] = useState(false);
  const [error, setError] = useState<string>();
  const [action, setAction] = useState<
    'skip' | 'output' | 'requeue' | 'writeOff' | 'notStarted'
  >('skip');
  const trigger = useRef<HTMLButtonElement>(null);
  const summary = useRef<HTMLParagraphElement>(null);
  const mutation = useMutation({
    mutationFn: async () => {
      if (action === 'skip')
        await skip(jobId, {
          artifactHash: query.data?.artifactHash ?? '',
          unitIds: selected,
        });
      else if (action === 'output')
        await confirmOutput(jobId, {
          artifactHash: query.data?.artifactHash ?? '',
          producedUnitIds: selected,
        });
      else if (action === 'notStarted')
        await confirmNotStarted(jobId, {
          artifactHash: query.data?.artifactHash ?? '',
        });
      else if (action === 'requeue') await requeue(jobId);
      else await writeOff(jobId);
    },
    onSuccess: async () => {
      setConfirm(false);
      setSelected([]);
      await query.refetch();
      invalidateProductionQueries(client);
    },
    onError: async (e) =>
      setError(await extractErrorDetail(e, 'Could not update print units.')),
  });
  if (query.isPending) return <Skeleton className="h-16" />;
  if (query.isError)
    return (
      <ErrorState
        title="Could not load print objects"
        onRetry={() => query.refetch()}
      />
    );
  const execution = query.data;
  if (!execution) return null;
  const units = execution.manifest?.units ?? [];
  const unavailable = new Set([
    ...(execution.plannedExclusions ?? []),
    ...(execution.requestedObjectIds ?? []),
    ...(execution.confirmedObjectIds ?? []),
  ]);
  const remaining = units.filter(
    (u) => !u.objectIds?.some((id) => unavailable.has(id)),
  );
  const pending =
    mutation.isPending ||
    (live &&
      !execution.observed &&
      !!execution.requestedObjectIds?.length &&
      !execution.requestError);
  const reconciling =
    status === 'Completed' && !execution.observed && !!execution.artifactHash;
  const outputKnown =
    execution.observed || status === 'Failed' || status === 'Cancelled';
  const missing = Math.max(0, (target ?? 0) - (produced ?? 0));
  const title =
    action === 'notStarted'
      ? 'Confirm this print did not start?'
      : action === 'output'
        ? 'Confirm finished units?'
        : action === 'requeue'
          ? 'Requeue missing units?'
          : action === 'writeOff'
            ? 'Write off missing units?'
            : 'Skip selected print units?';
  return (
    <Card>
      <CardHeader>
        <CardTitle>Print units</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        <p ref={summary} tabIndex={-1} aria-live="polite">
          {live
            ? `${execution.selectedUnitIds?.length ?? 0} planned units`
            : `${produced ?? 0} / ${target ?? execution.selectedUnitIds?.length ?? 0} units produced`}{' '}
          · {execution.plannedExclusions?.length ?? 0} objects excluded before
          printing
        </p>
        {live &&
          execution.canSkipObjects === false &&
          execution.skipUnavailableReason && (
            <Alert>
              <AlertDescription>
                {execution.skipUnavailableReason}
              </AlertDescription>
            </Alert>
          )}
        {execution.manifest?.dialect === 'Bambu' &&
          !!execution.plannedExclusions?.length && (
            <Alert>
              <AlertDescription>
                This plate uses printer-side object exclusions. Apply the same
                exclusions if you print the downloaded file outside OpenFarm.
              </AlertDescription>
            </Alert>
          )}
        {status === 'Queued' && execution.startRequested && (
          <Button
            ref={trigger}
            variant="outline"
            disabled={mutation.isPending}
            onClick={() => {
              setError(undefined);
              setAction('notStarted');
              setConfirm(true);
            }}
          >
            Confirm print did not start
          </Button>
        )}
        {!execution.observed && (
          <Alert>
            <AlertDescription>
              {execution.requestError ??
                'Waiting for the printer to confirm object exclusions. Output remains unverified until confirmation.'}
            </AlertDescription>
          </Alert>
        )}
        <PrintObjectOutlines
          objects={execution.manifest?.objects ?? []}
          selected={units
            .filter((u) => selected.includes(u.id ?? ''))
            .flatMap((u) => u.objectIds ?? [])}
        />
        <FieldGroup>
          {units.map((u) => {
            const excluded = u.objectIds?.some((id) => unavailable.has(id));
            const names = u.objectIds
              ?.map(
                (id) =>
                  execution.manifest?.objects?.find((o) => o.id === id)?.name ??
                  id,
              )
              .join(', ');
            return (
              <Field key={u.id} orientation="horizontal">
                <Checkbox
                  id={`skip-unit-${u.id}`}
                  checked={selected.includes(u.id ?? '')}
                  disabled={
                    pending ||
                    (live && execution.canSkipObjects === false) ||
                    (!live && !reconciling) ||
                    (!reconciling && excluded) ||
                    !execution.selectedUnitIds?.includes(u.id ?? '')
                  }
                  onCheckedChange={(checked) =>
                    setSelected((ids) =>
                      checked
                        ? [...ids, u.id ?? '']
                        : ids.filter((id) => id !== u.id),
                    )
                  }
                />
                <FieldLabel htmlFor={`skip-unit-${u.id}`}>
                  Unit {u.id}: {names}
                  {excluded && !reconciling ? ' · excluded' : ''}
                </FieldLabel>
              </Field>
            );
          })}
        </FieldGroup>
        <p role="status" aria-live="polite">
          {selected.length} units selected
        </p>
        {live && (
          <div className="flex flex-wrap gap-2">
            <Button
              ref={trigger}
              variant="destructive"
              disabled={
                pending ||
                execution.canSkipObjects === false ||
                selected.length === 0 ||
                selected.length >= remaining.length
              }
              onClick={() => {
                setError(undefined);
                setAction('skip');
                setConfirm(true);
              }}
            >
              Skip selected units
            </Button>
            <Button
              variant="outline"
              disabled={pending || selected.length === 0}
              onClick={() => {
                setSelected([]);
                summary.current?.focus();
              }}
            >
              Clear selection
            </Button>
          </div>
        )}
        {live && execution.requestError && (
          <Button
            variant="outline"
            disabled={mutation.isPending}
            onClick={() => {
              setSelected(
                units
                  .filter((u) =>
                    u.objectIds?.some((id) =>
                      execution.requestedObjectIds?.includes(id),
                    ),
                  )
                  .map((u) => u.id ?? ''),
              );
              setAction('skip');
              setConfirm(true);
            }}
          >
            Retry skip request
          </Button>
        )}
        <p className="text-sm text-muted-foreground">
          Filament usage is estimated from object extrusion and the progress
          observed when objects were skipped.
        </p>
        {reconciling && (
          <>
            <p>
              Select only the complete units physically finished on this plate.
            </p>
            <Button
              ref={trigger}
              disabled={mutation.isPending}
              onClick={() => {
                setError(undefined);
                setAction('output');
                setConfirm(true);
              }}
            >
              Confirm produced units
            </Button>
          </>
        )}
        {canResolve && outputKnown && (
          <div className="flex flex-wrap gap-2">
            <Button
              disabled={mutation.isPending || !canRequeue}
              onClick={() => {
                setError(undefined);
                setAction('requeue');
                setConfirm(true);
              }}
            >
              Requeue {missing} missing units
            </Button>
            <Button
              variant="outline"
              disabled={mutation.isPending}
              onClick={() => {
                setError(undefined);
                setAction('writeOff');
                setConfirm(true);
              }}
            >
              Write off {missing} missing units
            </Button>
          </div>
        )}
        <ConfirmDialog
          open={confirm}
          onOpenChange={setConfirm}
          title={title}
          description={
            action === 'notStarted'
              ? 'Confirm that the printer is idle, no objects from this job were printed, and its bed is clear. The attempt will be cancelled so its missing quantity can be requeued.'
              : action === 'output'
                ? `Confirm that ${selected.length} complete units finished. The remainder will stay missing.`
                : action === 'skip'
                  ? `${selected.length} complete units will be treated as missing. You can requeue the missing quantity after this print ends.`
                  : `${missing} missing units will be ${action === 'requeue' ? 'queued for replacement' : 'written off'}. Units already produced are retained.`
          }
          confirmLabel={
            action === 'notStarted'
              ? 'Confirm not started'
              : action === 'output'
                ? 'Confirm output'
                : action === 'requeue'
                  ? 'Requeue missing units'
                  : action === 'writeOff'
                    ? 'Write off'
                    : 'Skip units'
          }
          destructive={
            action === 'skip' ||
            action === 'writeOff' ||
            action === 'notStarted'
          }
          isPending={mutation.isPending}
          closeOnConfirm={false}
          onConfirm={() => mutation.mutate()}
          error={error}
          returnFocusRef={trigger}
          fallbackFocusRef={summary}
        />
      </CardContent>
    </Card>
  );
}
