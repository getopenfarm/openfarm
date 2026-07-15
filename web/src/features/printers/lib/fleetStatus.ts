import type { FleetPrinterResponse } from '@/lib/api/generated/endpoints.schemas';

export interface FleetStatusCounts {
  total: number;
  printing: number;
  awaitingClear: number;
  idle: number;
  offline: number;
}

/** Buckets the live fleet by status/reachability — shared by FarmPulse (header) and the
 * Dashboard's KPI row so both read the exact same counts off the same query cache. */
export function deriveFleetStatusCounts(
  printers: FleetPrinterResponse[],
): FleetStatusCounts {
  const counts: FleetStatusCounts = {
    total: printers.length,
    printing: 0,
    awaitingClear: 0,
    idle: 0,
    offline: 0,
  };

  for (const printer of printers) {
    if (printer.isReachable === false) {
      counts.offline += 1;
      continue;
    }
    if (printer.status === 'Printing') counts.printing += 1;
    else if (printer.status === 'AwaitingClear') counts.awaitingClear += 1;
    else counts.idle += 1;
  }

  return counts;
}
