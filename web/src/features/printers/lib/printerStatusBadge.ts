import type { PrinterStatus } from '@/components/printer-tile/status';
import { OFFLINE_META, STATUS_META } from '@/components/printer-tile/status';
import type { BadgeKind } from '@/components/status-badge/kinds';

/** Bridges the printer status vocabulary into StatusBadge's generic `kind` prop.
 * Offline is checked first — it's tracked separately from PrinterStatus. */
export function printerStatusToBadge(
  status: PrinterStatus,
  isReachable: boolean,
): { kind: BadgeKind; label: string } {
  if (!isReachable) {
    return { kind: 'red', label: OFFLINE_META.label };
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
