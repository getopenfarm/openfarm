import { CheckCircle2Icon } from 'lucide-react';
import { Link } from 'react-router';
import { PrinterTile } from '@/components/printer-tile/PrinterTile';
import type { PrinterStatus } from '@/components/printer-tile/status';
import { Button } from '@/components/ui/button';
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
        <div key={printer.id} className="group relative">
          <Link to={`/printers/${printer.id}`} className="block">
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
            />
          </Link>
          {printer.status === 'AwaitingClear' && (
            <div className="absolute top-2 right-11 flex gap-1 opacity-0 transition-opacity group-hover:opacity-100">
              <Button
                variant="ghost"
                size="icon-sm"
                aria-label={`Mark ${printer.name} ready`}
                onClick={() => onMarkReady(printer.id ?? '')}
              >
                <CheckCircle2Icon className="size-4" />
              </Button>
            </div>
          )}
        </div>
      ))}
    </div>
  );
}
