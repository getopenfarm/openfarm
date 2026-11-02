import type { PrinterStatus } from '@/components/printer-tile/status';
import { OFFLINE_META, STATUS_META } from '@/components/printer-tile/status';
import type { BadgeKind } from '@/components/status-badge/kinds';
import type { PrintJobSendProgress } from '@/features/production-runs/hooks/sendProgress';

function sendingLabel({ stage, percent }: PrintJobSendProgress) {
  if (stage === 'Slicing') return 'Slicing';
  if (stage === 'Uploading') return `Sending ${percent ?? 0}%`;
  if (stage === 'Retrying') return 'Send retrying';
  return 'Starting';
}

/** Bridges the printer status vocabulary into StatusBadge's generic `kind` prop.
 * Offline is checked first — it's tracked separately from PrinterStatus. An Idle printer
 * that a job is being sent to shows the send instead (display only, never a status). */
export function printerStatusToBadge(
  status: PrinterStatus,
  isReachable: boolean,
  sending?: PrintJobSendProgress,
): { kind: BadgeKind; label: string } {
  if (!isReachable) {
    return { kind: 'red', label: OFFLINE_META.label };
  }
  if (status === 'Idle' && sending) {
    return { kind: 'blue', label: sendingLabel(sending) };
  }

  const meta = STATUS_META[status];
  const kind: BadgeKind =
    status === 'Printing'
      ? 'green'
      : status === 'AwaitingClear'
        ? 'amber'
        : 'zinc';

  return { kind, label: meta.label };
}
