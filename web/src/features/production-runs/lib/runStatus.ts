import type { BadgeKind } from '@/components/status-badge/kinds';

/** Complete means every plate is done or written off and the run waits for its Close sign-off. */
export const RUN_STATUS: Record<string, { kind: BadgeKind; label: string }> = {
  Active: { kind: 'blue', label: 'Active' },
  Complete: { kind: 'green', label: 'Complete' },
  Closed: { kind: 'zinc', label: 'Closed' },
  Cancelled: { kind: 'zinc', label: 'Cancelled' },
};

export const isOpenRun = (status?: string) =>
  status === 'Active' || status === 'Complete';

/**
 * Close is the sign-off: for a Complete run, or an Active one with no finite work (unlimited lines only), which never
 * completes on its own.
 */
export const canCloseRun = (status?: string, hasFiniteLines = true) =>
  status === 'Complete' || (status === 'Active' && !hasFiniteLines);
