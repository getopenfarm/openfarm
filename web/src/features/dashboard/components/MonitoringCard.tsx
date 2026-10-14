import { AlertTriangle } from 'lucide-react';
import { Link } from 'react-router';
import { StatusBadge } from '@/components/status-badge/StatusBadge';
import { Button } from '@/components/ui/button';
import { Progress } from '@/components/ui/progress';
import {
  CameraSurface,
  cameraSupported,
} from '@/features/printers/components/CameraSurface';
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
  const badge = monitoringStatus(printer);
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
  return (
    <article
      aria-label={`${printer.name} monitor`}
      className="flex min-w-0 flex-col overflow-hidden rounded-xl border bg-card text-card-foreground shadow-sm"
    >
      <header className="flex h-10 items-center justify-between gap-2 px-3">
        <Link
          to={`/printers/${printer.id}`}
          className="min-w-0 truncate text-sm font-medium hover:underline"
          title={printer.name}
        >
          {printer.name ?? 'Unnamed printer'}
        </Link>
        {canClear ? (
          <button
            type="button"
            className="max-w-[55%] cursor-pointer overflow-hidden rounded-full transition-opacity hover:opacity-80 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring disabled:cursor-wait disabled:opacity-50"
            aria-label={`Mark ${printer.name} ready`}
            title="Mark printer ready"
            disabled={readyPending}
            onClick={onMarkReady}
          >
            <StatusBadge
              kind={badge.kind}
              label={readyPending ? 'Saving…' : badge.label}
              className="max-w-full overflow-hidden"
            />
          </button>
        ) : (
          <span className="max-w-[55%] overflow-hidden" title={badge.label}>
            <StatusBadge
              kind={badge.kind}
              label={badge.label}
              className="max-w-full overflow-hidden"
            />
          </span>
        )}
      </header>
      <div className="flex min-w-0 gap-2 px-3">
        <CameraSurface
          printerId={printer.id ?? ''}
          name={printer.name}
          isReachable={printer.isReachable}
          supported={cameraSupported(printer.modelCode)}
          compact
          className="min-w-0 flex-1 self-start rounded-md"
        />
        <FilamentRail printer={printer} />
      </div>
      <div className="flex flex-col gap-2 px-3 pt-3 pb-2">
        <p
          className={cn(
            'truncate text-xs font-medium',
            !hasJob && 'text-muted-foreground',
          )}
          title={hasJob ? (printer.currentJobName ?? undefined) : undefined}
        >
          {hasJob ? printer.currentJobName || 'Unnamed job' : 'No active job'}
        </p>
        <div className="flex h-4 items-center gap-2 text-[11px]">
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
