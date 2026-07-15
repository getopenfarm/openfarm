import { describe, expect, it } from 'vitest';
import type { FleetPrinterResponse } from '@/lib/api/generated/endpoints.schemas';
import {
  type MaintenanceFlagSeed,
  matchMaintenanceToFleet,
} from './matchMaintenanceToFleet';

const X1C_PRINTER: FleetPrinterResponse = {
  id: 'printer-1',
  name: 'Farm X1C #1',
  modelCode: 'X1C',
};

const MK4_PRINTER: FleetPrinterResponse = {
  id: 'printer-2',
  name: 'Farm MK4',
  modelCode: 'MK4',
};

describe('matchMaintenanceToFleet', () => {
  it('returns [] when there are no printers', () => {
    const seeds: MaintenanceFlagSeed[] = [
      {
        pattern: 'X1C',
        task: 'Belt tension check',
        detailText: 'x',
        severity: 'overdue',
      },
    ];

    expect(matchMaintenanceToFleet([], seeds)).toEqual([]);
  });

  it('matches a seed to a printer whose name or modelCode contains the pattern', () => {
    const seeds: MaintenanceFlagSeed[] = [
      {
        pattern: 'X1C',
        task: 'Belt tension check',
        detailText: 'overdue by 2 days',
        severity: 'overdue',
      },
      {
        pattern: 'MK4',
        task: 'Nozzle check',
        detailText: 'due soon',
        severity: 'due',
      },
    ];

    const flags = matchMaintenanceToFleet([X1C_PRINTER, MK4_PRINTER], seeds);

    expect(flags).toHaveLength(2);
    expect(flags[0]).toEqual({
      printerId: 'printer-1',
      printerName: 'Farm X1C #1',
      printerModel: 'X1C',
      task: 'Belt tension check',
      detailText: 'overdue by 2 days',
      severity: 'overdue',
    });
    expect(flags[1]).toEqual({
      printerId: 'printer-2',
      printerName: 'Farm MK4',
      printerModel: 'MK4',
      task: 'Nozzle check',
      detailText: 'due soon',
      severity: 'due',
    });
  });

  it('does not reuse a printer already claimed by an earlier seed in the same call', () => {
    const seeds: MaintenanceFlagSeed[] = [
      { pattern: 'X1C', task: 'Task A', detailText: 'a', severity: 'overdue' },
      { pattern: 'X1C', task: 'Task B', detailText: 'b', severity: 'due' },
    ];

    // Only one X1C printer exists, but two seeds target it — the second seed
    // must fall back to a different printer rather than double-booking.
    const flags = matchMaintenanceToFleet([X1C_PRINTER, MK4_PRINTER], seeds);

    expect(flags).toHaveLength(2);
    expect(flags[0].printerId).toBe('printer-1');
    expect(flags[1].printerId).toBe('printer-2');
  });

  it('falls back round-robin across real printers when no pattern matches at all', () => {
    const noMatchPrinters: FleetPrinterResponse[] = [
      { id: 'a', name: 'Generic Printer A', modelCode: 'GP-A' },
      { id: 'b', name: 'Generic Printer B', modelCode: 'GP-B' },
    ];
    const seeds: MaintenanceFlagSeed[] = [
      {
        pattern: 'X1C',
        task: 'Belt tension check',
        detailText: 'x',
        severity: 'overdue',
      },
      {
        pattern: 'X1C',
        task: 'Nozzle inspection',
        detailText: 'y',
        severity: 'due',
      },
      { pattern: 'X1C', task: 'Lubrication', detailText: 'z', severity: 'due' },
      {
        pattern: 'MK4',
        task: 'Nozzle check',
        detailText: 'w',
        severity: 'due',
      },
    ];

    const flags = matchMaintenanceToFleet(noMatchPrinters, seeds);

    // Every seed still produces a flag, matched against some real printer.
    expect(flags).toHaveLength(4);
    for (const flag of flags) {
      expect(['a', 'b']).toContain(flag.printerId);
      expect(['Generic Printer A', 'Generic Printer B']).toContain(
        flag.printerName,
      );
    }
    // Round robin: index 0 -> printer a, index 1 -> printer b, index 2 -> a, index 3 -> b
    expect(flags.map((f) => f.printerId)).toEqual(['a', 'b', 'a', 'b']);
  });

  it('handles a single-printer fleet by round-robining every unmatched seed onto it', () => {
    const singlePrinter: FleetPrinterResponse[] = [
      { id: 'solo', name: 'Solo Printer', modelCode: 'SP-1' },
    ];
    const seeds: MaintenanceFlagSeed[] = [
      { pattern: 'X1C', task: 'Task A', detailText: 'a', severity: 'overdue' },
      { pattern: 'MK4', task: 'Task B', detailText: 'b', severity: 'due' },
    ];

    const flags = matchMaintenanceToFleet(singlePrinter, seeds);

    expect(flags).toHaveLength(2);
    expect(flags.every((f) => f.printerId === 'solo')).toBe(true);
  });
});
