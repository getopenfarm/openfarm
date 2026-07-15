import type { BadgeKind } from '@/components/status-badge/kinds';

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
): string {
  const humanizedEvent = humanizeIdentifier(eventType);
  const humanizedEntity = humanizeIdentifier(entityType);

  if (humanizedEvent.startsWith(humanizedEntity)) {
    return capitalize(humanizedEvent);
  }
  return capitalize(`${humanizedEntity} ${humanizedEvent}`);
}

/** Best-effort color coding for the activity feed's dot — based on common verb suffixes in
 * the backend's EventType strings (e.g. "PrinterRegistered", "PrinterRemoved"). */
export function activityEventKind(eventType: string): BadgeKind {
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
