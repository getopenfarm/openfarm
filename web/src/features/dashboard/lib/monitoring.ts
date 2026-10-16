import {
  getStatusLabel,
  OFFLINE_META,
  type PrinterStatus,
} from '@/components/printer-tile/status';
import type { BadgeKind } from '@/components/status-badge/kinds';
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
  if (!printer.isReachable) return { label: OFFLINE_META.label, kind: 'zinc' };
  const raw = printer.telemetry?.reportedState?.trim();
  const state = reportedState(printer);
  const label =
    (raw && reportedStateName(raw)) ||
    (printer.status ? getStatusLabel(printer.status as PrinterStatus) : '') ||
    'Unknown';
  return {
    label,
    kind:
      state === 'FAILED'
        ? 'red'
        : state === 'PAUSE' || printer.status === 'AwaitingClear'
          ? 'amber'
          : printer.status === 'Printing'
            ? 'green'
            : 'zinc',
  };
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
