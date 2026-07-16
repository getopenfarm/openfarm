import { Swatch } from '@/components/swatch/Swatch';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import type {
  AmsTrayResponse,
  AmsUnitResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { cn } from '@/lib/utils';

export interface AmsSlotGridProps {
  amsUnits: AmsUnitResponse[];
}

interface SlotRow {
  key: string;
  label: string;
  tray: AmsTrayResponse;
}

function toSlotRows(units: AmsUnitResponse[]): SlotRow[] {
  return units.flatMap((unit) => {
    const unitLetter = String.fromCharCode(65 + (unit.unitIndex ?? 0));
    return (unit.trays ?? []).map((tray) => ({
      key: `${unit.unitIndex}-${tray.slotIndex}`,
      label: `${unitLetter}${(tray.slotIndex ?? 0) + 1}`,
      tray,
    }));
  });
}

function remainColor(pct: number | null | undefined): string {
  if (pct == null) return 'text-muted-foreground';
  if (pct <= 15) return 'text-status-offline';
  if (pct <= 30) return 'text-status-attention';
  return 'text-muted-foreground';
}

export function AmsSlotGrid({ amsUnits }: AmsSlotGridProps) {
  const rows = toSlotRows(amsUnits);

  return (
    <Card className="gap-0 py-0">
      <CardHeader className="border-b px-[18px] py-3.5">
        <CardTitle className="text-sm font-semibold">Filament — AMS</CardTitle>
      </CardHeader>
      <CardContent className="p-0">
        {rows.length === 0 ? (
          <p className="px-[18px] py-7 text-center text-sm text-muted-foreground">
            No AMS unit attached
          </p>
        ) : (
          <ul>
            {rows.map(({ key, label, tray }) => (
              <li
                key={key}
                className={cn(
                  'flex items-center gap-3 border-b px-[18px] py-3 last:border-b-0',
                  tray.active && 'bg-accent/40',
                )}
              >
                <span className="w-8 shrink-0 font-mono text-[11px] text-muted-foreground">
                  {label}
                </span>
                {tray.empty ? (
                  <Swatch className="bg-transparent" />
                ) : (
                  <Swatch colorHex={`#${(tray.colorHex ?? '').slice(0, 6)}`} />
                )}
                <div className="min-w-0 flex-1">
                  <p className="truncate text-[13px] font-medium">
                    {tray.empty ? 'Empty' : (tray.materialType ?? 'Filament')}
                  </p>
                  {!tray.empty && tray.subBrand && (
                    <p className="truncate text-[11.5px] text-muted-foreground">
                      {tray.subBrand}
                    </p>
                  )}
                </div>
                {!tray.empty && tray.remainPercent != null && (
                  <span
                    className={cn(
                      'font-mono text-xs',
                      remainColor(tray.remainPercent),
                    )}
                  >
                    {tray.remainPercent}%
                  </span>
                )}
              </li>
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  );
}
