import type { PrinterDetailTelemetryResponse } from '@/lib/api/generated/endpoints.schemas';
import { CameraSurface, cameraSupported } from './CameraSurface';

export interface CameraViewProps {
  printerId: string;
  telemetry: PrinterDetailTelemetryResponse;
  isReachable: boolean;
  modelCode?: string;
}

function formatTemp(value: number | null | undefined): string {
  return value == null ? '—' : `${value}°C`;
}

export function CameraView({
  printerId,
  telemetry,
  isReachable,
  modelCode,
}: CameraViewProps) {
  return (
    <div className="overflow-hidden rounded-xl bg-card ring-1 ring-foreground/10">
      <CameraSurface
        printerId={printerId}
        isReachable={isReachable}
        supported={cameraSupported(modelCode)}
      />
      <div className="grid grid-cols-2 border-t sm:grid-cols-4">
        <div className="border-r px-4 py-3.5">
          <p className="text-xs text-muted-foreground">Nozzle</p>
          <p className="mt-1 font-mono text-lg font-semibold">
            {formatTemp(telemetry.nozzleTempC)}
          </p>
        </div>
        <div className="border-r px-4 py-3.5">
          <p className="text-xs text-muted-foreground">Bed</p>
          <p className="mt-1 font-mono text-lg font-semibold">
            {formatTemp(telemetry.bedTempC)}
          </p>
        </div>
        <div className="border-r px-4 py-3.5">
          <p className="text-xs text-muted-foreground">Progress</p>
          <p className="mt-1 font-mono text-lg font-semibold text-status-printing">
            {telemetry.printProgressPercent == null
              ? '—'
              : `${telemetry.printProgressPercent}%`}
          </p>
        </div>
        <div className="px-4 py-3.5">
          <p className="text-xs text-muted-foreground">Stage</p>
          <p className="mt-1 text-lg font-semibold">
            {telemetry.currentStageLabel ?? '—'}
          </p>
        </div>
      </div>
    </div>
  );
}
