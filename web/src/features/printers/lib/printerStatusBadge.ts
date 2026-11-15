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
 * that a job is being sent to shows the send instead, and an Idle printer that cannot take
 * any job yet shows "Needs setup" (both display only, never a status). `solid` marks the states
 * that need the operator: Awaiting clear and Needs setup. `setup` marks Needs setup, the chip that leads to the
 * slicer settings. */
export function printerStatusToBadge(
  status: PrinterStatus,
  isReachable: boolean,
  sending?: PrintJobSendProgress,
  setupIssues?: readonly string[] | null,
): { kind: BadgeKind; label: string; solid?: boolean; setup?: boolean } {
  if (!isReachable) {
    return { kind: 'red', label: OFFLINE_META.label };
  }
  if (status === 'Idle' && sending) {
    return { kind: 'blue', label: sendingLabel(sending) };
  }
  if (status === 'Idle' && setupIssues?.length) {
    return { kind: 'amber', label: 'Needs setup', solid: true, setup: true };
  }

  const meta = STATUS_META[status];
  const kind: BadgeKind =
    status === 'Printing'
      ? 'green'
      : status === 'AwaitingClear'
        ? 'amber'
        : 'zinc';

  return { kind, label: meta.label, solid: status === 'AwaitingClear' };
}
