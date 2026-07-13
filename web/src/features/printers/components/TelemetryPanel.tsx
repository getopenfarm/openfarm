import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import type { PrinterDetailTelemetryResponse } from '@/lib/api/generated/endpoints.schemas';

export interface TelemetryPanelProps {
  telemetry: PrinterDetailTelemetryResponse;
}

function formatTemp(value: number | null | undefined): string {
  return value == null ? '—' : `${value}°C`;
}

export function TelemetryPanel({ telemetry }: TelemetryPanelProps) {
  return (
    <Card>
      <CardHeader>
        <CardTitle>Telemetry</CardTitle>
      </CardHeader>
      <CardContent className="grid grid-cols-2 gap-4 sm:grid-cols-4">
        <div>
          <p className="text-xs text-muted-foreground">Nozzle</p>
          <p className="font-mono text-lg">
            {formatTemp(telemetry.nozzleTempC)}
          </p>
        </div>
        <div>
          <p className="text-xs text-muted-foreground">Bed</p>
          <p className="font-mono text-lg">{formatTemp(telemetry.bedTempC)}</p>
        </div>
        <div>
          <p className="text-xs text-muted-foreground">Progress</p>
          <p className="font-mono text-lg">
            {telemetry.printProgressPercent == null
              ? '—'
              : `${telemetry.printProgressPercent}%`}
          </p>
        </div>
        <div>
          <p className="text-xs text-muted-foreground">Stage</p>
          <p className="font-mono text-lg">
            {telemetry.currentStageLabel ?? '—'}
          </p>
        </div>
      </CardContent>
    </Card>
  );
}
