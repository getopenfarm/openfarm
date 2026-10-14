import { reportedStateLabel } from '@/features/printers/lib/reportedState';
import type {
  ActivityLogDetails,
  PrinterAlert,
} from '@/lib/api/generated/endpoints.schemas';

export function AlertDescription({ alert }: { alert: PrinterAlert }) {
  return (
    <span>
      {alert.message} <span className="font-mono">{alert.code}</span>
      {alert.troubleshootingUrl && (
        <a
          className="ml-2 underline"
          href={alert.troubleshootingUrl}
          target="_blank"
          rel="noreferrer"
        >
          Troubleshooting
        </a>
      )}
    </span>
  );
}

export function ActivityDetails({
  details,
}: {
  details?: ActivityLogDetails | null;
}) {
  if (!details) return null;
  const before = details.previousAlerts ?? [];
  const after = details.activeAlerts ?? [];
  const key = (alert: PrinterAlert) => `${alert.source}:${alert.code}`;
  const changes = [
    ...after
      .filter(
        (a) =>
          !before.some(
            (b) =>
              key(a) === key(b) &&
              a.message === b.message &&
              a.severity === b.severity &&
              a.troubleshootingUrl === b.troubleshootingUrl,
          ),
      )
      .map((alert) => ({ alert, cleared: false })),
    ...before
      .filter((a) => !after.some((b) => key(a) === key(b)))
      .map((alert) => ({ alert, cleared: true })),
  ];
  return (
    <div className="mt-1 flex flex-col gap-1 text-xs text-muted-foreground">
      {details.reportedState && (
        <p>
          {details.previousState
            ? `${reportedStateLabel(details.previousState)} → `
            : ''}
          {reportedStateLabel(details.reportedState)}
        </p>
      )}
      {changes.map(({ alert, cleared }) => (
        <p key={`${key(alert)}:${cleared}`}>
          {cleared ? 'Cleared' : (alert.severity ?? 'Unknown')}:{' '}
          <AlertDescription alert={alert} />
        </p>
      ))}
    </div>
  );
}
