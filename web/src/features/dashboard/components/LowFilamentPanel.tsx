import { Swatch } from '@/components/swatch/Swatch';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { cn } from '@/lib/utils';
import { LOW_FILAMENT_SPOOLS } from '../data/mockDashboardFilament';

function remainingColorClass(remainingPercent: number): string {
  if (remainingPercent < 15) return 'text-kind-red';
  if (remainingPercent < 25) return 'text-kind-amber';
  return 'text-kind-green';
}

// Mock panel — no Filament backend yet. See ../data/mockDashboardFilament.ts.
export function LowFilamentPanel() {
  return (
    <Card>
      <CardHeader>
        <CardTitle>Low filament</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        {LOW_FILAMENT_SPOOLS.length === 0 ? (
          <p className="py-6 text-center text-sm text-muted-foreground">
            No spools running low.
          </p>
        ) : (
          LOW_FILAMENT_SPOOLS.map((spool) => (
            <div key={spool.id} className="flex items-center gap-3">
              <Swatch colorHex={spool.colorHex} />
              <div className="min-w-0 flex-1">
                <p className="truncate text-sm font-medium">
                  {spool.colorName}
                </p>
                <p className="truncate text-xs text-muted-foreground">
                  {spool.location}
                </p>
              </div>
              <span
                className={cn(
                  'shrink-0 font-mono text-sm font-medium tabular-nums',
                  remainingColorClass(spool.remainingPercent),
                )}
              >
                {spool.remainingPercent}%
              </span>
            </div>
          ))
        )}
      </CardContent>
    </Card>
  );
}
