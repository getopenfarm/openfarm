import { Progress } from '@/components/ui/progress';
import type { MaintenanceEnrollmentResponse } from '@/lib/api/generated/endpoints.schemas';
import { formatDate } from '@/lib/formatDateTime';
import { cn } from '@/lib/utils';
import { progressCaption, progressPercent, STATE_BAR } from '../maintenance';

/** Bar for the trigger closest to due, colored by state, with "used / interval · left" below. */
export function EnrollmentProgress({
  enrollment,
}: {
  enrollment: MaintenanceEnrollmentResponse;
}) {
  return (
    <div className="flex min-w-40 flex-col gap-1.5">
      <Progress
        value={progressPercent(enrollment)}
        className="h-1.5"
        indicatorClassName={STATE_BAR[enrollment.state ?? 'Ok']}
        aria-label={`${enrollment.taskName} progress`}
      />
      <span
        className={cn(
          'font-mono text-xs',
          enrollment.state === 'Overdue'
            ? 'text-kind-red'
            : 'text-muted-foreground',
        )}
      >
        {progressCaption(enrollment)}
      </span>
    </div>
  );
}

/** Last logged completion, or "Baseline <date>" when the cycle start was only set. */
export function LastDone({
  enrollment,
}: {
  enrollment: MaintenanceEnrollmentResponse;
}) {
  return (
    <span className="text-sm whitespace-nowrap text-muted-foreground">
      {enrollment.lastDoneAtUtc
        ? formatDate(enrollment.lastDoneAtUtc)
        : `Baseline ${formatDate(enrollment.cycleStartedAtUtc ?? '')}`}
    </span>
  );
}
