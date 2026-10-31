import { useQueries } from '@tanstack/react-query';
import {
  ArrowUpToLineIcon,
  CheckIcon,
  PencilIcon,
  PlusIcon,
} from 'lucide-react';
import { useState } from 'react';
import { useNavigate, useParams } from 'react-router';
import { BackLink } from '@/components/back-link/BackLink';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { KpiStrip, type KpiStripCell } from '@/components/kpi-card/KpiStrip';
import { PermissionButton as Button } from '@/components/PermissionButton';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import { StatusBadge } from '@/components/status-badge/StatusBadge';
import {
  Card,
  CardAction,
  CardContent,
  CardHeader,
  CardTitle,
} from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import type {
  PartResponse,
  ProductionRunDetailResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { getOpenFarmApiEndpointsPartsGetPartEndpointQueryOptions } from '@/lib/api/generated/parts/parts';
import { useOpenFarmApiEndpointsPrintersListFleetEndpoint } from '@/lib/api/generated/printers/printers';
import { useOpenFarmApiEndpointsProductionRunsGetProductionRunEndpoint } from '@/lib/api/generated/production-runs/production-runs';
import { formatMinutes } from '@/lib/formatMinutes';
import { formatWeight } from '@/lib/formatUnits';
import { cn } from '@/lib/utils';
import { RunAttentionCard } from './components/RunAttentionCard';
import { RunLines } from './components/RunLines';
import { SendToQueueDialog } from './components/SendToQueueDialog';
import { useProductionHub } from './hooks/useProductionHub';
import { useRunActions } from './hooks/useRunActions';
import { describeDue } from './lib/dueDate';
import { canCloseRun, isOpenRun, RUN_STATUS } from './lib/runStatus';

const units = (n: number) => Number(n.toFixed(2));

/** Units, plates, and the machine time and filament the waiting and printing plates still need, from their estimates. */
function summarize(run: ProductionRunDetailResponse): KpiStripCell[] {
  const lines = run.lines ?? [];
  const finite = lines
    .filter((l) => l.quantity != null)
    .flatMap((l) => l.requirements ?? []);
  const produced = lines
    .filter((l) => l.quantity == null)
    .reduce((sum, l) => sum + (l.cumulativeProducedUnits ?? 0), 0);
  let target = 0;
  let done = 0;
  let writtenOff = 0;
  let minutes = 0;
  let grams = 0;
  let unestimated = false;
  const plates = {
    completed: 0,
    printing: 0,
    queued: 0,
    needsAction: 0,
    writtenOff: 0,
  };
  for (const r of finite) {
    target += r.quantity ?? 0;
    done += Math.min(r.completedUnits ?? 0, r.quantity ?? 0);
    writtenOff += r.writtenOffUnits ?? 0;
    for (const key of Object.keys(plates) as (keyof typeof plates)[])
      plates[key] += r.plates?.[key] ?? 0;
    const left = (r.plates?.queued ?? 0) + (r.plates?.printing ?? 0);
    if (left > 0 && r.estimatedMinutesPerPlate == null) unestimated = true;
    minutes += left * (r.estimatedMinutesPerPlate ?? 0);
    grams += left * (r.estimatedGramsPerPlate ?? 0);
  }
  const total = Object.values(plates).reduce((a, b) => a + b, 0);

  return [
    finite.length > 0
      ? {
          label: 'Units',
          value: `${units(done)}/${units(target)}`,
          sub: writtenOff > 0 ? `${writtenOff} written off` : undefined,
          dotClassName: 'bg-(--chart-completed)',
        }
      : { label: 'Units produced', value: String(produced) },
    {
      label: 'Plates',
      value: `${plates.completed}/${total}`,
      sub:
        [
          plates.printing > 0 ? `${plates.printing} printing` : null,
          plates.queued > 0 ? `${plates.queued} waiting` : null,
          plates.needsAction > 0 ? `${plates.needsAction} failed` : null,
        ]
          .filter(Boolean)
          .join(' · ') || undefined,
      dotClassName:
        plates.needsAction > 0
          ? 'bg-(--chart-failed)'
          : 'bg-(--chart-printing)',
    },
    {
      label: 'Machine time left',
      value: formatMinutes(minutes),
      sub: unestimated
        ? 'Partial: some parts have no estimate'
        : 'Sum of plate estimates',
    },
    {
      label: 'Filament left',
      value: formatWeight(grams),
      sub: unestimated ? 'Partial' : undefined,
    },
  ];
}

export function ProductionRunDetailPage() {
  const { id = '' } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [adding, setAdding] = useState(false);
  const actions = useRunActions();
  useProductionHub();

  const runQuery =
    useOpenFarmApiEndpointsProductionRunsGetProductionRunEndpoint(id);
  const run = runQuery.data?.data as ProductionRunDetailResponse | undefined;
  const printers =
    useOpenFarmApiEndpointsPrintersListFleetEndpoint().data?.data?.printers ??
    [];
  const partIds = [
    ...new Set(
      (run?.lines ?? []).flatMap((l) =>
        (l.requirements ?? [])
          .filter((r) => r.sourcePartAvailable && r.partId)
          .map((r) => r.partId ?? ''),
      ),
    ),
  ];
  const partQueries = useQueries({
    queries: partIds.map((partId) =>
      getOpenFarmApiEndpointsPartsGetPartEndpointQueryOptions(partId),
    ),
  });
  const thumbnailOf = (partId?: string) =>
    (
      partQueries[partIds.indexOf(partId ?? '')]?.data?.data as
        | PartResponse
        | undefined
    )?.thumbnails?.[0]?.thumbnailBase64;
  const printerName = (printerId?: string | null) =>
    printers.find((p) => p.id === printerId)?.name ?? undefined;

  const backLink = (
    <BackLink to="/production-runs" className="mb-1">
      Back to runs
    </BackLink>
  );

  if (!run?.id) {
    return (
      <Page>
        <PageHeader above={backLink} title="Run" />
        {runQuery.isLoading ? (
          <Skeleton className="h-64 w-full" />
        ) : runQuery.isError &&
          (runQuery.error as unknown as { status?: number } | null)?.status !==
            404 ? (
          <ErrorState
            title="Could not load run"
            onRetry={() => runQuery.refetch()}
          />
        ) : (
          <EmptyState
            title="Run not found"
            description="Removed or invalid link."
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

  const status = RUN_STATUS[run.status ?? ''];
  const open = isOpenRun(run.status);
  const hasFinite = (run.lines ?? []).some((l) => l.quantity != null);
  const waiting = (run.lines ?? [])
    .flatMap((l) => l.requirements ?? [])
    .some((r) => (r.plates?.queued ?? 0) > 0);
  const due = run.dueDate ? describeDue(run.dueDate) : null;
  const created = run.createdAtUtc
    ? new Date(run.createdAtUtc).toLocaleDateString(undefined, {
        day: 'numeric',
        month: 'short',
      })
    : null;

  return (
    <Page>
      <PageHeader
        above={backLink}
        title={
          <span className="flex flex-wrap items-center gap-3">
            {run.displayLabel}
            {status && <StatusBadge kind={status.kind} label={status.label} />}
          </span>
        }
        description={
          <span className="flex flex-wrap gap-x-1">
            {due ? (
              <>
                <span>Due {due.label}</span>
                {open && (
                  <span
                    className={cn(
                      due.overdue && 'font-medium text-status-attention',
                      due.soon && 'text-foreground',
                    )}
                  >
                    · {due.relative}
                  </span>
                )}
              </>
            ) : (
              <span>No due date</span>
            )}
            {created && <span>· Created {created}</span>}
          </span>
        }
        action={
          <div className="flex flex-wrap gap-2">
            {open && waiting && (
              <Button
                permission="print-jobs:reorder"
                variant="outline"
                disabled={actions.isPending}
                onClick={() => actions.moveToTop(id)}
              >
                <ArrowUpToLineIcon data-icon="inline-start" /> Move to top
              </Button>
            )}
            {open && (
              <Button
                permission="production-runs:edit"
                variant="outline"
                onClick={() => navigate(`/production-runs/${id}/edit`)}
              >
                <PencilIcon data-icon="inline-start" /> Edit
              </Button>
            )}
            {canCloseRun(run.status, hasFinite) && (
              <Button
                permission="production-runs:close"
                disabled={actions.isPending}
                onClick={() => actions.close(id)}
              >
                <CheckIcon data-icon="inline-start" />
                {hasFinite ? 'Close run' : 'Stop backfill'}
              </Button>
            )}
            {open && (
              <Button
                permission="production-runs:cancel"
                variant="destructive"
                onClick={() => actions.cancel(id, run.displayLabel ?? 'run')}
              >
                Cancel run
              </Button>
            )}
          </div>
        }
      />

      <KpiStrip cells={summarize(run)} />

      <RunAttentionCard run={run} printerName={printerName} />

      <Card className="gap-2">
        <CardHeader>
          <CardTitle className="text-base">Lines</CardTitle>
          {open && (
            <CardAction>
              <Button
                permission="production-runs:create"
                size="sm"
                variant="outline"
                onClick={() => setAdding(true)}
              >
                <PlusIcon data-icon="inline-start" /> Add part or assembly
              </Button>
            </CardAction>
          )}
        </CardHeader>
        <CardContent>
          <RunLines
            lines={run.lines ?? []}
            thumbnailOf={thumbnailOf}
            printerName={printerName}
          />
        </CardContent>
      </Card>

      {run.notes && (
        <Card className="gap-2">
          <CardHeader>
            <CardTitle className="text-base">Notes</CardTitle>
          </CardHeader>
          <CardContent>
            <p className="max-w-prose text-sm whitespace-pre-line">
              {run.notes}
            </p>
          </CardContent>
        </Card>
      )}

      {adding && (
        <SendToQueueDialog runId={id} onClose={() => setAdding(false)} />
      )}
      {actions.cancelDialog}
    </Page>
  );
}
