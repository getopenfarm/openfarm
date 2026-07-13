import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import type { AmsUnitResponse } from '@/lib/api/generated/endpoints.schemas';
import { cn } from '@/lib/utils';

export interface AmsSlotGridProps {
  amsUnits: AmsUnitResponse[];
}

export function AmsSlotGrid({ amsUnits }: AmsSlotGridProps) {
  return (
    <Card>
      <CardHeader>
        <CardTitle>AMS</CardTitle>
      </CardHeader>
      <CardContent>
        {amsUnits.length === 0 ? (
          <p className="text-sm text-muted-foreground">No AMS unit attached.</p>
        ) : (
          <div className="flex flex-col gap-4">
            {amsUnits.map((unit) => (
              <div key={unit.unitIndex}>
                <p className="mb-2 text-xs text-muted-foreground">
                  AMS {unit.unitIndex}
                </p>
                <div className="grid grid-cols-4 gap-2">
                  {(unit.trays ?? []).map((tray) => (
                    <div
                      key={tray.slotIndex}
                      className={cn(
                        'flex flex-col items-center gap-1 rounded-lg border p-2 text-center',
                        tray.active && 'ring-2 ring-primary',
                      )}
                    >
                      <span
                        className="size-6 rounded-full ring-1 ring-foreground/15"
                        style={{
                          backgroundColor: tray.empty
                            ? 'transparent'
                            : `#${(tray.colorHex ?? '').slice(0, 6)}`,
                        }}
                        aria-hidden="true"
                      />
                      <p className="text-xs">
                        {tray.empty ? 'Empty' : tray.materialType}
                      </p>
                      {!tray.empty && tray.remainPercent != null && (
                        <p className="text-xs text-muted-foreground">
                          {tray.remainPercent}%
                        </p>
                      )}
                    </div>
                  ))}
                </div>
              </div>
            ))}
          </div>
        )}
      </CardContent>
    </Card>
  );
}
