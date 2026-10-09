import type { BadgeKind } from '@/components/status-badge/kinds';
import { reportedStateLabel } from '@/features/printers/lib/reportedState';
import type { ActivityLogDetails } from '@/lib/api/generated/endpoints.schemas';

function humanizeIdentifier(value: string): string {
  return value.replace(/([a-z0-9])([A-Z])/g, '$1 $2').toLowerCase();
}

function capitalize(value: string): string {
  return value.charAt(0).toUpperCase() + value.slice(1);
}

/** Turns e.g. entityType="Printer"/eventType="PrinterConnectionEdited" into "Printer
 * connection edited", avoiding the entity name doubling up when the event already names it. */
export function describeActivityEvent(
  entityType: string,
  eventType: string,
  details?: ActivityLogDetails | null,
): string {
  if (
    entityType === 'Printer' &&
    eventType === 'PrinterStatusObserved' &&
    details?.reportedState
  ) {
    return `Printer reported ${reportedStateLabel(details.reportedState)}`;
  }
  const humanizedEvent = humanizeIdentifier(eventType);
  const humanizedEntity = humanizeIdentifier(entityType);

  if (humanizedEvent.startsWith(humanizedEntity)) {
    return capitalize(humanizedEvent);
  }
  return capitalize(`${humanizedEntity} ${humanizedEvent}`);
}

/** Best-effort color coding for the activity feed's dot — based on common verb suffixes in
 * the backend's EventType strings (e.g. "PrinterRegistered", "PrinterRemoved"). */
export function activityEventKind(
  eventType: string,
  details?: ActivityLogDetails | null,
): BadgeKind {
  const alerts = details?.activeAlerts ?? [];
  if (alerts.some((a) => a.severity === 'Error')) return 'red';
  if (alerts.some((a) => a.severity === 'Warning')) return 'amber';
  if (details?.reportedState?.trim().toUpperCase() === 'FAILED') return 'red';
  if (details?.reportedState?.trim().toUpperCase() === 'PAUSE') return 'amber';
  const type = eventType.toLowerCase();
  if (type.includes('removed') || type.includes('failed')) return 'red';
  if (
    type.includes('registered') ||
    type.includes('created') ||
    type.includes('markedready')
  ) {
    return 'green';
  }
  if (type.includes('edited') || type.includes('updated')) return 'blue';
  return 'zinc';
}
