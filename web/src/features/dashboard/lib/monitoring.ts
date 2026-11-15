import type { PrinterStatus } from '@/components/printer-tile/status';
import type { BadgeKind } from '@/components/status-badge/kinds';
import { printerStatusToBadge } from '@/features/printers/lib/printerStatusBadge';
import { reportedStateName } from '@/features/printers/lib/reportedState';
import type { PrintJobSendProgress } from '@/features/production-runs/hooks/sendProgress';
import type {
  FleetPrinterResponse,
  PrinterDetailResponse,
} from '@/lib/api/generated/endpoints.schemas';

export type MonitoredPrinter = FleetPrinterResponse &
  Pick<PrinterDetailResponse, 'telemetry' | 'feedReadings'>;
export function reportedState(printer: MonitoredPrinter) {
  return printer.telemetry?.reportedState?.trim().toUpperCase();
}

/**
 * The reported state worth showing over the farm status. A printer keeps reporting FAILED after the operator marks it ready
 * (it reports again only when something happens on it), so a state counts only while the farm status is the one it maps to:
 * Paused while Printing, Failed while Awaiting clear. Once the operator moves the printer on, the card follows the farm status.
 */
function attentionState(printer: MonitoredPrinter): 'PAUSE' | 'FAILED' | null {
  if (!printer.isReachable) return null;
  const state = reportedState(printer);
  if (state === 'PAUSE' && printer.status === 'Printing') return state;
  if (state === 'FAILED' && printer.status === 'AwaitingClear') return state;
  return null;
}

export function monitoringStatus(
  printer: MonitoredPrinter,
  sending?: PrintJobSendProgress,
): {
  label: string;
  kind: BadgeKind;
  solid?: boolean;
  setup?: boolean;
} {
  // Farm status matches the printer page; only Paused/Failed override it, while they still apply.
  const state = attentionState(printer);
  if (state)
    return {
      label: reportedStateName(state),
      kind: state === 'FAILED' ? 'red' : 'amber',
      solid: state === 'FAILED',
    };
  return printerStatusToBadge(
    (printer.status ?? 'Idle') as PrinterStatus,
    printer.isReachable ?? false,
    sending,
    printer.setupIssues,
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
  const messages = [
    ...[...errors, ...warnings].map(
      (alert) => alert.message || alert.code || 'Printer alert',
    ),
    ...(printer.setupIssues ?? []),
  ];
  // No stand-in message for a bare Paused/Failed: the chip already says it.
  return messages.length
    ? {
        kind:
          errors.length || attentionState(printer) === 'FAILED'
            ? 'red'
            : 'amber',
        messages: [...new Set(messages)],
      }
    : null;
}
