import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Progress } from '@/components/ui/progress';
import type { PrinterDetailTelemetryResponse } from '@/lib/api/generated/endpoints.schemas';

export interface CurrentJobCardProps {
  telemetry: PrinterDetailTelemetryResponse;
}

function MetaField({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <p className="text-xs text-muted-foreground">{label}</p>
      <p className="mt-0.5 font-mono text-sm font-medium">{value}</p>
    </div>
  );
}

export function CurrentJobCard({ telemetry }: CurrentJobCardProps) {
  const progress = telemetry.printProgressPercent;
  const isActive = progress != null;

  return (
    <Card className="gap-0 py-0">
      <CardHeader className="border-b px-[18px] py-3.5">
        <CardTitle className="text-sm font-semibold">Current job</CardTitle>
      </CardHeader>
      <CardContent className="px-[18px] py-4">
        {isActive ? (
          <div className="flex flex-col gap-4">
            <div className="flex flex-col gap-2.5">
              <div className="flex items-baseline justify-between gap-2.5">
                <span className="truncate text-sm">
                  {telemetry.currentStageLabel ?? 'Printing'}
                </span>
                <span className="shrink-0 font-mono text-sm font-medium text-status-printing">
                  {progress}%
                </span>
              </div>
              <Progress
                value={progress}
                className="h-1.5"
                indicatorClassName="bg-status-printing"
              />
            </div>
            <div className="flex gap-8">
              <MetaField label="Layer" value="—" />
              <MetaField label="Quantity" value="—" />
              <MetaField label="Started" value="—" />
            </div>
          </div>
        ) : (
          <p className="py-6 text-center text-sm text-muted-foreground">
            No active print
          </p>
        )}
      </CardContent>
    </Card>
  );
}
