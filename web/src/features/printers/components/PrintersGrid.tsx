import { CheckCircle2Icon } from 'lucide-react';
import { Link } from 'react-router';
import { PrinterTile } from '@/components/printer-tile/PrinterTile';
import type { PrinterStatus } from '@/components/printer-tile/status';
import { Button } from '@/components/ui/button';
import type { FleetPrinterResponse } from '@/lib/api/generated/endpoints.schemas';

const PLACEHOLDER_FILAMENT_COLOR = '#8892a6';

interface PrintersGridProps {
  printers: FleetPrinterResponse[];
  onMarkReady: (id: string) => void;
}

export function PrintersGrid({ printers, onMarkReady }: PrintersGridProps) {
  return (
    <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-3">
      {printers.map((printer) => (
        <div key={printer.id} className="group relative">
          <Link to={`/printers/${printer.id}`} className="block">
            <PrinterTile
              name={printer.name ?? ''}
              status={(printer.status ?? 'Idle') as PrinterStatus}
              isReachable={printer.isReachable ?? false}
              filamentColorHex={PLACEHOLDER_FILAMENT_COLOR}
              progressPercent={printer.printProgressPercent ?? 0}
              nozzleTempC={printer.nozzleTempC}
              bedTempC={printer.bedTempC}
              etaMinutes={null}
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
