import { StatusBadge } from '@/components/status-badge/StatusBadge';
import { AlertDescription } from '@/features/activity-log/components/ActivityDetails';
import type { PrinterDetailTelemetryResponse } from '@/lib/api/generated/endpoints.schemas';
import { formatDateTime } from '@/lib/formatDateTime';
import { reportedStateLabel } from '../lib/reportedState';

export function PrinterReportedStatus({
  telemetry,
  isReachable,
}: {
  telemetry?: PrinterDetailTelemetryResponse;
  isReachable: boolean;
}) {
  const alerts = telemetry?.activeAlerts ?? [];
  return (
    <div className="flex flex-col gap-2">
      <p className="text-sm text-muted-foreground">
        {isReachable ? 'Reported state' : 'Last-known reported state'}:{' '}
        {telemetry?.reportedState
          ? reportedStateLabel(telemetry.reportedState)
          : 'Not yet reported'}
        {telemetry?.stateObservedAtUtc && (
          <span className="ml-2 text-xs">
            Observed {formatDateTime(telemetry.stateObservedAtUtc)}
          </span>
        )}
      </p>
      {alerts.length > 0 && (
        <section
          className="flex flex-col gap-2 rounded-lg border p-3"
          aria-label={
            isReachable ? 'Active printer alerts' : 'Last-known printer alerts'
          }
        >
          {!isReachable && (
            <p className="text-xs text-muted-foreground">
              Last-known alerts — printer is offline
            </p>
          )}
          {alerts.map((alert) => (
            <div
              key={`${alert.source}:${alert.code}`}
              className="flex items-start gap-2 text-sm"
            >
              <StatusBadge
                kind={
                  alert.severity === 'Error'
                    ? 'red'
                    : alert.severity === 'Warning'
                      ? 'amber'
                      : 'zinc'
                }
                label={alert.severity ?? 'Unknown'}
              />
              <AlertDescription alert={alert} />
            </div>
          ))}
        </section>
      )}
    </div>
  );
}
