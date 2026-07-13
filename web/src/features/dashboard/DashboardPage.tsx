import {
  PrinterTile,
  type PrinterTileProps,
} from '@/components/printer-tile/PrinterTile';

// TODO(Phase 1 follow-up): replace sampleFleet with a Fleet API query (Orval-generated
// hook) plus SignalR-driven live updates — the printers feature itself now owns that
// wiring (web/src/features/printers/PrintersPage.tsx); the dashboard's own summary view
// is a later phase.
const sampleFleet: PrinterTileProps[] = [
  {
    name: 'Printer 01',
    status: 'Printing',
    isReachable: true,
    filamentColorHex: '#47bfff',
    progressPercent: 62,
    nozzleTempC: 220,
    bedTempC: 60,
    etaMinutes: 47,
  },
  {
    name: 'Printer 02',
    status: 'Printing',
    isReachable: true,
    filamentColorHex: '#863bff',
    progressPercent: 18,
    nozzleTempC: 215,
    bedTempC: 58,
    etaMinutes: 132,
  },
  {
    name: 'Printer 03',
    status: 'AwaitingClear',
    isReachable: true,
    filamentColorHex: '#f5a623',
    progressPercent: 84,
    nozzleTempC: 205,
    bedTempC: 55,
    etaMinutes: 12,
  },
  {
    name: 'Printer 04',
    status: 'AwaitingClear',
    isReachable: true,
    filamentColorHex: '#3dd68c',
    progressPercent: 100,
    nozzleTempC: 0,
    bedTempC: 0,
    etaMinutes: null,
  },
  {
    name: 'Printer 05',
    status: 'Idle',
    isReachable: true,
    filamentColorHex: '#f1eefa',
    progressPercent: 0,
    nozzleTempC: 25,
    bedTempC: 24,
    etaMinutes: null,
  },
  {
    name: 'Printer 06',
    status: 'Idle',
    isReachable: false,
    filamentColorHex: '#ff5d5d',
    progressPercent: 0,
    nozzleTempC: 0,
    bedTempC: 0,
    etaMinutes: null,
  },
];

export function DashboardPage() {
  return (
    <div className="flex flex-col gap-6">
      <div>
        <h2 className="font-heading text-2xl font-semibold">Farm Overview</h2>
        <p className="text-sm text-muted-foreground">
          Sample data — live fleet telemetry lands in Phase 1.
        </p>
      </div>
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-3">
        {sampleFleet.map((printer) => (
          <PrinterTile key={printer.name} {...printer} />
        ))}
      </div>
    </div>
  );
}
