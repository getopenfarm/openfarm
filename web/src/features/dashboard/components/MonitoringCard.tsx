import {
  AlertTriangle,
  HeaterIcon,
  type LucideIcon,
  PauseIcon,
  SquareIcon,
  ThermometerIcon,
} from 'lucide-react';
import { Link } from 'react-router';
import { TargetThumbnail } from '@/components/library-item-picker/LibraryItemPicker';
import { StatusBadge } from '@/components/status-badge/StatusBadge';
import { Button } from '@/components/ui/button';
import { Progress } from '@/components/ui/progress';
import { usePrinterSendProgress } from '@/features/printers/hooks/usePrinterSendProgress';
import {
  JobRemoteControls,
  ROUND_CONTROL,
} from '@/features/production-runs/components/JobRemoteControls';
import { useOpenFarmApiEndpointsPrintJobsGetPrintJobEndpoint } from '@/lib/api/generated/print-jobs/print-jobs';
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
  readyPending,
}: {
  printer: MonitoredPrinter;
  detailError: boolean;
  onRetry: () => void;
  onMarkReady: () => void;
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
  const progress = printer.printProgressPercent;
  const canClear = printer.isReachable && printer.status === 'AwaitingClear';
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
      className="flex min-w-0 flex-col overflow-hidden rounded-xl border bg-card text-card-foreground shadow-sm"
    >
      <header className="flex h-10 items-center justify-between gap-2 px-3">
        <Link
          to={`/printers/${printer.id}`}
          className="min-w-0 truncate text-sm font-medium hover:underline"
        >
          {printer.name ?? 'Unnamed printer'}
        </Link>
        {canClear ? (
          <button
            type="button"
            className="max-w-[55%] cursor-pointer overflow-hidden rounded-full transition-opacity hover:opacity-80 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring disabled:cursor-wait disabled:opacity-50"
            aria-label={`Mark ${printer.name} ready`}
            disabled={readyPending}
            onClick={onMarkReady}
          >
            <StatusBadge
              kind={badge.kind}
              label={readyPending ? 'Marking ready…' : badge.label}
              className="max-w-full overflow-hidden"
            />
          </button>
        ) : (
          <span className="max-w-[55%] overflow-hidden">
            <StatusBadge
              kind={badge.kind}
              label={badge.label}
              className="max-w-full overflow-hidden"
            />
          </span>
        )}
      </header>
      {/* Square thumbnail (plate images are square); the rail spans down to the progress row. */}
      <div className="grid min-w-0 grid-cols-[6rem_minmax(0,1fr)_auto] grid-rows-[6rem_auto] gap-x-3 gap-y-3 px-3 pb-3">
        <TargetThumbnail
          kind="Part"
          thumbnailBase64={thumbnail}
          className={cn('size-24', !hasJob && 'opacity-40')}
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
          {hasJob ? (
            <>
              <Progress
                value={progress ?? 0}
                className={cn(
                  'h-1 flex-1',
                  !printer.isReachable && 'opacity-40',
                )}
                indicatorClassName="bg-status-printing"
              />
              <span className="font-mono tabular-nums text-muted-foreground">
                {progress == null ? '—' : `${progress}%`}
              </span>
              <span className="ml-auto shrink-0 font-mono tabular-nums">
                {running
                  ? minutes == null
                    ? 'ETA —'
                    : `${formatMinutes(minutes)} left`
                  : 'ETA —'}
              </span>
            </>
          ) : (
            <span className="sr-only">No print in progress</span>
          )}
        </div>
      </div>
      {(issues || detailError) && (
        <div
          className={cn(
            'mt-auto flex min-h-8 items-center gap-1.5 px-3 py-1',
            issues?.kind === 'red'
              ? 'bg-kind-red/8 text-kind-red'
              : issues
                ? 'bg-kind-amber/8 text-kind-amber'
                : detailError
                  ? 'bg-muted text-muted-foreground'
                  : '',
          )}
        >
          {issues ? (
            <>
              <AlertTriangle className="size-3 shrink-0" aria-hidden="true" />
              <DashboardHint
                label={`${printer.name}: ${issues.messages.join(' · ')}`}
                className="flex min-w-0 flex-1 items-center gap-1 text-left text-[11px] focus-visible:outline-ring"
                details={
                  <>
                    <p className="font-medium">{printer.name}</p>
                    <ul className="flex flex-col gap-1">
                      {issues.messages.map((message) => (
                        <li key={message}>{message}</li>
                      ))}
                    </ul>
                    {detailError && <p>Detailed status unavailable</p>}
                  </>
                }
              >
                <span className="truncate">{issues.messages[0]}</span>
                {issues.messages.length > 1 && (
                  <span className="shrink-0">
                    +{issues.messages.length - 1}
                  </span>
                )}
              </DashboardHint>
            </>
          ) : detailError ? (
            <span className="min-w-0 flex-1 truncate text-[11px]">
              Status unavailable
            </span>
          ) : null}
          {detailError ? (
            <Button
              size="xs"
              variant="ghost"
              className="ml-auto"
              onClick={onRetry}
            >
              Retry
            </Button>
          ) : null}
        </div>
      )}
    </article>
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
