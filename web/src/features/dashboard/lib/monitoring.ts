import type { PrinterStatus } from '@/components/printer-tile/status';
import type { BadgeKind } from '@/components/status-badge/kinds';
import { printerStatusToBadge } from '@/features/printers/lib/printerStatusBadge';
import { reportedStateName } from '@/features/printers/lib/reportedState';
import type {
  FleetPrinterResponse,
  PrinterDetailResponse,
} from '@/lib/api/generated/endpoints.schemas';

export type MonitoredPrinter = FleetPrinterResponse &
  Pick<PrinterDetailResponse, 'telemetry' | 'amsUnits' | 'externalSpoolTray'>;
export function reportedState(printer: MonitoredPrinter) {
  return printer.telemetry?.reportedState?.trim().toUpperCase();
}

export function monitoringStatus(printer: MonitoredPrinter): {
  label: string;
  kind: BadgeKind;
} {
  // Farm status matches the printer page; only Paused/Failed override it.
  const state = reportedState(printer);
  if (printer.isReachable && (state === 'PAUSE' || state === 'FAILED'))
    return {
      label: reportedStateName(state),
      kind: state === 'FAILED' ? 'red' : 'amber',
    };
  return printerStatusToBadge(
    (printer.status ?? 'Idle') as PrinterStatus,
    printer.isReachable ?? false,
  );
}

export function printerIssues(
  printer: MonitoredPrinter,
): { kind: 'red' | 'amber'; messages: string[] } | null {
  if (!printer.isReachable) return null;
  const alerts = printer.telemetry?.activeAlerts ?? [];
  const errors = alerts.filter((alert) => alert.severity === 'Error');
  const warnings = alerts.filter(
    (alert) => !['Error', 'Information', 'Info'].includes(alert.severity ?? ''),
  );
  const messages = [...errors, ...warnings].map(
    (alert) => alert.message || alert.code || 'Printer alert',
  );
  const state = reportedState(printer);
  if (!messages.length && (state === 'FAILED' || state === 'PAUSE'))
    messages.push('Check printer');
  return messages.length
    ? {
        kind: errors.length || state === 'FAILED' ? 'red' : 'amber',
        messages: [...new Set(messages)],
      }
    : null;
}
