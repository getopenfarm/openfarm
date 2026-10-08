import { Link } from 'react-router';
import { PrinterTile } from '@/components/printer-tile/PrinterTile';
import type { PrinterStatus } from '@/components/printer-tile/status';
import type { FleetPrinterResponse } from '@/lib/api/generated/endpoints.schemas';
import type { DensityMode } from '@/lib/preferences/types';

interface PrintersGridProps {
  printers: FleetPrinterResponse[];
  density?: DensityMode;
  onMarkReady: (id: string) => void;
}

// Column counts per density — Small packs well over 9 per page, Large gives each card room.
const GRID_COLUMNS: Record<DensityMode, string> = {
  small: 'grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5',
  medium: 'grid-cols-1 sm:grid-cols-2 xl:grid-cols-3',
  large: 'grid-cols-1 md:grid-cols-2',
};

export function PrintersGrid({
  printers,
  density = 'medium',
  onMarkReady,
}: PrintersGridProps) {
  return (
    <div className={`grid gap-4 ${GRID_COLUMNS[density]}`}>
      {printers.map((printer) => (
        <div key={printer.id} className="relative">
          <Link
            to={`/printers/${printer.id}`}
            aria-label={`Open ${printer.name ?? 'printer'}`}
            className="absolute inset-0 z-10 rounded-xl focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
          />
          <PrinterTile
            name={printer.name ?? ''}
            status={(printer.status ?? 'Idle') as PrinterStatus}
            isReachable={printer.isReachable ?? false}
            filamentColorHex={printer.filamentColorHex ?? null}
            progressPercent={printer.printProgressPercent ?? 0}
            nozzleTempC={printer.nozzleTempC}
            bedTempC={printer.bedTempC}
            etaMinutes={printer.remainingTimeMinutes ?? null}
            jobName={printer.currentJobName ?? null}
            stageLabel={printer.currentStageLabel ?? null}
            size={density}
            onMarkReady={() => onMarkReady(printer.id ?? '')}
          />
        </div>
      ))}
    </div>
  );
}
