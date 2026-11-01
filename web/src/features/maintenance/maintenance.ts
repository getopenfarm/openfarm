import type { QueryClient } from '@tanstack/react-query';
import type { BadgeKind } from '@/components/status-badge/kinds';
import type {
  ListFleetResponse,
  MaintenanceEnrollmentResponse,
  MaintenanceEnrollmentsResponse,
  MaintenanceTaskResponse,
  OpenFarmApiEndpointsMaintenanceListMaintenanceEnrollmentsEndpointParams,
} from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsMaintenanceListMaintenanceEnrollmentsEndpoint } from '@/lib/api/generated/maintenance/maintenance';
import { useOpenFarmApiEndpointsPrintersListFleetEndpoint } from '@/lib/api/generated/printers/printers';
import { useAllowed } from '@/lib/auth/AccessContext';
import { pluralize } from '@/lib/pluralize';

/** Every maintenance list: tasks, enrollments and logs. */
export function invalidateMaintenanceQueries(queryClient: QueryClient) {
  void queryClient.invalidateQueries({
    predicate: (query) =>
      typeof query.queryKey[0] === 'string' &&
      query.queryKey[0].startsWith('/api/v1/maintenance'),
  });
}

/**
 * Enrollments with their due state. A running print grows the open printing stretch without any hub event,
 * so the list refetches every minute while a printer is printing. Nothing is fetched without maintenance:view.
 */
export function useMaintenanceEnrollments(
  params?: OpenFarmApiEndpointsMaintenanceListMaintenanceEnrollmentsEndpointParams,
) {
  const canView = useAllowed('maintenance:view');
  const fleet = useOpenFarmApiEndpointsPrintersListFleetEndpoint({
    query: { enabled: canView },
  }).data?.data as ListFleetResponse | undefined;
  const printing = !!fleet?.printers?.some((p) => p.status === 'Printing');
  const query =
    useOpenFarmApiEndpointsMaintenanceListMaintenanceEnrollmentsEndpoint(
      params,
      {
        query: {
          enabled: canView,
          refetchInterval: printing ? 60_000 : false,
        },
      },
    );
  const items =
    (query.data?.data as MaintenanceEnrollmentsResponse | undefined)?.items ??
    NONE;
  return { query, items };
}
const NONE: MaintenanceEnrollmentResponse[] = [];

export const STATE_BADGE: Record<string, { kind: BadgeKind; label: string }> = {
  Overdue: { kind: 'red', label: 'Overdue' },
  DueSoon: { kind: 'amber', label: 'Due soon' },
  Ok: { kind: 'green', label: 'OK' },
};

export const STATE_BAR: Record<string, string> = {
  Overdue: 'bg-kind-red',
  DueSoon: 'bg-kind-amber',
  Ok: 'bg-kind-green',
};

export const needsAttention = (e: MaintenanceEnrollmentResponse) =>
  e.state === 'Overdue' || e.state === 'DueSoon';

const UNIT: Record<string, string> = { Hours: 'h', Days: 'd', Prints: '' };
const num = (n: number) => String(Number(n.toFixed(Math.abs(n) < 100 ? 1 : 0)));
const amount = (trigger: string, n: number) =>
  trigger === 'Prints'
    ? pluralize(Math.round(n), 'print')
    : `${num(n)} ${UNIT[trigger]}`;

/** Bar fill for the trigger closest to due, 0–100. */
export function progressPercent(e: MaintenanceEnrollmentResponse) {
  const interval = e.interval ?? 0;
  return interval > 0 ? Math.min(100, ((e.used ?? 0) / interval) * 100) : 0;
}

/** "≥ 212 / 320 h · Overdue by 12 h"; "≥" when untracked time makes hours a lower bound. */
export function progressCaption(e: MaintenanceEnrollmentResponse) {
  const trigger = e.drivingTrigger ?? 'Hours';
  const remaining = e.remaining ?? 0;
  const lowerBound = trigger === 'Hours' && (e.untrackedHours ?? 0) > 0;
  const used = `${num(e.used ?? 0)} / ${amount(trigger, e.interval ?? 0)}`;
  return `${lowerBound ? '≥ ' : ''}${used} · ${
    remaining < 0
      ? `Overdue by ${amount(trigger, -remaining)}`
      : `${amount(trigger, remaining)} left`
  }`;
}

/** "320 h · 90 d · 500 prints": whichever comes first. */
export function intervalsLabel(
  t: Pick<
    MaintenanceTaskResponse,
    'everyPrintHours' | 'everyDays' | 'everyPrints'
  >,
) {
  return [
    t.everyPrintHours != null && amount('Hours', t.everyPrintHours),
    t.everyDays != null && amount('Days', t.everyDays),
    t.everyPrints != null && amount('Prints', t.everyPrints),
  ]
    .filter(Boolean)
    .join(' · ');
}

/** Today as a date input value (local). */
export const todayInput = () => new Date().toLocaleDateString('en-CA');

/** A date input value as an ISO instant at local start of day. */
export const startOfDayIso = (date: string) =>
  new Date(`${date}T00:00`).toISOString();

/** An ISO instant as a date input value (local). */
export const toDateInput = (iso: string) =>
  new Date(iso).toLocaleDateString('en-CA');
