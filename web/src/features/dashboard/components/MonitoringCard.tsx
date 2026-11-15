import {
  AlertTriangle,
  HeaterIcon,
  type LucideIcon,
  PauseIcon,
  RefreshCw,
  SquareIcon,
  ThermometerIcon,
} from 'lucide-react';
import { Link } from 'react-router';
import { TargetThumbnail } from '@/components/library-item-picker/LibraryItemPicker';
import { StatusBadge } from '@/components/status-badge/StatusBadge';
import { Button } from '@/components/ui/button';
import { Progress } from '@/components/ui/progress';
import { SLICER_SETTINGS_PERMISSION } from '@/features/printers/components/SlicerSettingsDialog';
import { usePrinterSendProgress } from '@/features/printers/hooks/usePrinterSendProgress';
import {
  JobRemoteControls,
  ROUND_CONTROL,
} from '@/features/production-runs/components/JobRemoteControls';
import { useOpenFarmApiEndpointsPrintJobsGetPrintJobEndpoint } from '@/lib/api/generated/print-jobs/print-jobs';
import { useAllowed } from '@/lib/auth/AccessContext';
import { formatMinutes } from '@/lib/formatMinutes';
import { cn } from '@/lib/utils';
import {
  type MonitoredPrinter,
  monitoringStatus,
  printerIssues,
  reportedState,
} from '../lib/monitoring';
import { DashboardHint } from './DashboardHint';
import { FilamentRail } from './FilamentRail';

export function MonitoringCard({
  printer,
  detailError,
  onRetry,
  onMarkReady,
  onSetup,
  readyPending,
}: {
  printer: MonitoredPrinter;
  detailError: boolean;
  onRetry: () => void;
  onMarkReady: () => void;
  /** Opens the printer's slicer settings: Needs setup is a shortcut to them. */
  onSetup: () => void;
  readyPending: boolean;
}) {
  const badge = monitoringStatus(
    printer,
    usePrinterSendProgress(printer.id ?? ''),
  );
  const issues = printerIssues(printer);
  const state = reportedState(printer);
  const hasJob =
    printer.status === 'Printing' || printer.status === 'AwaitingClear';
  const running =
    printer.isReachable &&
    printer.status === 'Printing' &&
    state !== 'PAUSE' &&
    state !== 'FAILED';
  const minutes = printer.remainingTimeMinutes;
  const shownProgress = hasJob ? printer.printProgressPercent : null;
  const canClear = printer.isReachable && printer.status === 'AwaitingClear';
  const canMarkReady = useAllowed('printers:mark-ready');
  const canEditSlicer = useAllowed(SLICER_SETTINGS_PERMISSION);
  const jobId = hasJob ? printer.currentPrintJobId : null;
  // One fetch per job: the thumbnail never changes. Printer-started prints have no job, so no thumbnail.
  const job = useOpenFarmApiEndpointsPrintJobsGetPrintJobEndpoint(jobId ?? '', {
    query: { enabled: Boolean(jobId), staleTime: Number.POSITIVE_INFINITY },
  });
  const thumbnail = jobId ? job.data?.data?.thumbnailBase64 : null;
  const controllable = Boolean(
    jobId && printer.isReachable && printer.status === 'Printing',
  );
  const controlHint = !printer.isReachable
    ? 'Printer offline'
    : printer.status !== 'Printing'
      ? 'No print running'
      : 'Started on the printer: control it there';
  return (
    <article
      aria-label={`${printer.name} monitor`}
      className={cn(
        'flex min-w-0 flex-col overflow-hidden rounded-xl border bg-card text-card-foreground shadow-sm',
        issues?.kind === 'red' && 'border-kind-red/50',
        issues?.kind === 'amber' && 'border-kind-amber/50',
      )}
    >
      {/* Alerts live in the header, never a footer: card height must not depend on them. */}
      <header className="flex h-10 items-center gap-2 px-3">
        <Link
          to={`/printers/${printer.id}`}
          className="mr-auto min-w-0 truncate text-sm font-medium hover:underline"
        >
          {printer.name ?? 'Unnamed printer'}
        </Link>
        <CardAlert
          printerName={printer.name ?? 'Unnamed printer'}
          issues={issues}
          detailError={detailError}
          onRetry={onRetry}
        />
        <StatusBadge
          kind={badge.kind}
          solid={badge.solid}
          label={readyPending ? 'Marking ready…' : badge.label}
          action={
            canClear && canMarkReady
              ? {
                  onClick: onMarkReady,
                  label: `Mark ${printer.name} ready`,
                  disabled: readyPending,
                }
              : badge.setup && canEditSlicer
                ? {
                    onClick: onSetup,
                    label: `Set up ${printer.name}`,
                  }
                : undefined
          }
          className="max-w-[55%] overflow-hidden"
        />
      </header>
      {/* Square thumbnail (plate images are square); the rail spans down to the progress row. */}
      <div className="grid min-w-0 grid-cols-[6rem_minmax(0,1fr)_auto] grid-rows-[6rem_auto] gap-x-3 gap-y-3 px-3 pb-3">
        <TargetThumbnail
          kind="Part"
          thumbnailBase64={thumbnail}
          className="size-24"
        />
        <div className="flex min-w-0 flex-col">
          <p
            className={cn(
              'min-w-0 truncate text-sm font-medium',
              !hasJob && 'text-muted-foreground',
            )}
            title={hasJob ? (printer.currentJobName ?? undefined) : undefined}
          >
            {hasJob ? printer.currentJobName || 'Unnamed job' : 'No active job'}
          </p>
          {/* Pinned to the bottom so a long name never pushes them around. */}
          <div className="mt-auto flex flex-col gap-1">
            <p className="flex gap-3 font-mono text-[11px] tabular-nums text-muted-foreground">
              <Temperature
                icon={ThermometerIcon}
                label="Nozzle"
                value={printer.isReachable ? printer.nozzleTempC : null}
              />
              <Temperature
                icon={HeaterIcon}
                label="Bed"
                value={printer.isReachable ? printer.bedTempC : null}
              />
            </p>
            <div>
              {controllable && jobId ? (
                <JobRemoteControls
                  jobId={jobId}
                  status={state === 'PAUSE' ? 'Paused' : 'Printing'}
                  round
                  name={printer.currentJobName ?? undefined}
                />
              ) : (
                // Always visible so every card has the same shape; the title says why it's off.
                <div className="flex items-center gap-1" title={controlHint}>
                  <Button
                    size="icon"
                    variant="ghost"
                    disabled
                    aria-label="Pause"
                    className={ROUND_CONTROL}
                  >
                    <PauseIcon className="fill-current" />
                  </Button>
                  <Button
                    size="icon"
                    variant="ghost"
                    disabled
                    aria-label="Stop print"
                    className={ROUND_CONTROL}
                  >
                    <SquareIcon className="fill-current" />
                  </Button>
                </div>
              )}
            </div>
          </div>
        </div>
        <div className="row-span-2 flex">
          <FilamentRail printer={printer} />
        </div>
        <div className="col-span-2 flex h-4 items-center gap-2 self-end text-[11px]">
          {/* Same row with or without a job, so every card has the same shape; idle shows an empty bar and dashes. */}
          <Progress
            value={shownProgress ?? 0}
            className={cn('h-1 flex-1', !printer.isReachable && 'opacity-40')}
            indicatorClassName="bg-status-printing"
          />
          <span className="font-mono tabular-nums text-muted-foreground">
            {shownProgress == null ? '—' : `${shownProgress}%`}
          </span>
          <span className="ml-auto shrink-0 font-mono tabular-nums">
            {running && minutes != null
              ? `${formatMinutes(minutes)} left`
              : 'ETA —'}
          </span>
        </div>
      </div>
    </article>
  );
}

/**
 * Sits beside the status chip: a round tinted button (same look as Pause / Stop) holding a solid triangle and,
 * past one message, the count. The popover carries the text: HMS messages run ~100 chars, no header width fits them.
 */
function CardAlert({
  printerName,
  issues,
  detailError,
  onRetry,
}: {
  printerName: string;
  issues: ReturnType<typeof printerIssues>;
  detailError: boolean;
  onRetry: () => void;
}) {
  if (!issues)
    return detailError ? (
      <Button
        size="icon-xs"
        variant="ghost"
        className="text-muted-foreground"
        aria-label="Retry status"
        title="Status unavailable"
        onClick={onRetry}
      >
        <RefreshCw aria-hidden="true" />
      </Button>
    ) : null;
  const { kind, messages } = issues;
  return (
    <DashboardHint
      label={`${printerName}: ${messages.join(' · ')}`}
      side="bottom"
      align="end"
      className={cn(
        'flex h-5 min-w-5 shrink-0 items-center justify-center gap-1 rounded-full text-[11px] font-semibold tabular-nums focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring',
        messages.length > 1 && 'px-1.5',
        kind === 'red'
          ? 'bg-kind-red/20 text-kind-red hover:bg-kind-red/30'
          : 'bg-kind-amber/20 text-kind-amber hover:bg-kind-amber/30',
      )}
      details={
        <>
          <p className="font-medium">{printerName}</p>
          <ul className="flex flex-col gap-1">
            {messages.map((message) => (
              <li key={message}>{message}</li>
            ))}
          </ul>
          {detailError && (
            <div className="flex items-center justify-between gap-2 text-muted-foreground">
              <p>Detailed status unavailable</p>
              <Button size="xs" variant="ghost" onClick={onRetry}>
                Retry
              </Button>
            </div>
          )}
        </>
      }
    >
      {/* Solid triangle: the fill is the severity color, the "!" is cut out in the card color. */}
      <AlertTriangle
        aria-hidden="true"
        strokeWidth={2.4}
        className="size-3 shrink-0 fill-current [&>path:not(:first-child)]:stroke-card"
      />
      {messages.length > 1 && <span>{messages.length}</span>}
    </DashboardHint>
  );
}

function Temperature({
  icon: Icon,
  label,
  value,
}: {
  icon: LucideIcon;
  label: string;
  value?: number | null;
}) {
  return (
    <span className="flex items-center gap-1" title={label}>
      <Icon className="size-3" aria-hidden="true" />
      <span className="sr-only">{label}</span>
      {value == null ? '—' : `${Math.round(value)}°`}
    </span>
  );
}
