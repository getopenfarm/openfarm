// Pure matching logic joining fabricated maintenance-flag seeds onto the real
// fleet list. No React/hooks here on purpose — the Dashboard feature (built
// separately, later) imports this same function to compute the same flags.

import type { FleetPrinterResponse } from '@/lib/api/generated/endpoints.schemas';

export interface MaintenanceFlagSeed {
  /** Substring to match against a printer's name or modelCode, e.g. "X1C". */
  pattern: string;
  task: string;
  detailText: string;
  severity: 'overdue' | 'due';
}

export interface MatchedMaintenanceFlag {
  printerId: string;
  printerName: string;
  printerModel: string;
  task: string;
  detailText: string;
  severity: 'overdue' | 'due';
}

function printerLabel(printer: FleetPrinterResponse): string {
  return `${printer.name ?? ''} ${printer.modelCode ?? ''}`.toLowerCase();
}

/**
 * For each seed, find a real printer whose name or modelCode contains the
 * seed's pattern (case-insensitive) that hasn't already been claimed by an
 * earlier seed in this call. If no printer matches a given seed's pattern —
 * entirely possible, since the seed patterns are illustrative guesses, not
 * guaranteed to exist in whatever fleet is actually registered — fall back to
 * assigning that seed round-robin across whatever real printers DO exist, so
 * the page still shows something meaningful instead of silently dropping the
 * flag. Returns [] when there are no printers at all.
 */
export function matchMaintenanceToFleet(
  printers: FleetPrinterResponse[],
  seeds: MaintenanceFlagSeed[],
): MatchedMaintenanceFlag[] {
  if (printers.length === 0) {
    return [];
  }

  const usedIndexes = new Set<number>();
  const flags: MatchedMaintenanceFlag[] = [];

  seeds.forEach((seed, seedIndex) => {
    const pattern = seed.pattern.toLowerCase();
    let matchIndex = printers.findIndex(
      (printer, index) =>
        !usedIndexes.has(index) && printerLabel(printer).includes(pattern),
    );

    if (matchIndex === -1) {
      matchIndex = seedIndex % printers.length;
    }

    usedIndexes.add(matchIndex);
    const printer = printers[matchIndex];

    flags.push({
      printerId: printer.id ?? `unknown-${matchIndex}`,
      printerName: printer.name ?? 'Unknown printer',
      printerModel: printer.modelCode ?? '',
      task: seed.task,
      detailText: seed.detailText,
      severity: seed.severity,
    });
  });

  return flags;
}
