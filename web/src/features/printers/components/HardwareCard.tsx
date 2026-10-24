import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import type {
  ExtruderResponse,
  PrinterModelResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { formatCelsius, formatMm } from '@/lib/formatUnits';

function nozzleText(extruder?: ExtruderResponse) {
  if (extruder?.nozzleDiameterMm == null) return 'Awaiting telemetry';
  return `${formatMm(extruder.nozzleDiameterMm)} (${extruder.nozzleConfirmed ? 'reported' : 'awaiting confirmation'})`;
}

/** What the printer is: the model's facts, then each extruder's nozzle as reported. */
export function HardwareCard({
  model,
  extruders = [],
}: {
  model?: PrinterModelResponse | null;
  extruders?: ExtruderResponse[];
}) {
  if (!model) return null;
  const labels = model.extruders ?? [];
  const rows: [string, string | undefined][] = [
    ['Model', model.name],
    ['Driver', model.driver],
    ['Camera', model.cameraSupported ? 'Supported' : 'Not supported'],
    ...labels.map((label, index): [string, string] => {
      const extruder = extruders.find((e) => e.index === index);
      // One nozzle's temperature is already in the status strip.
      const temp =
        labels.length > 1 && extruder?.tempC != null
          ? ` · ${formatCelsius(extruder.tempC)}`
          : '';
      return [label, `${nozzleText(extruder)}${temp}`];
    }),
  ];
  return (
    <Card className="gap-0 py-0">
      <CardHeader className="border-b px-[18px] py-3.5">
        <CardTitle className="text-sm font-semibold">Hardware</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-2 px-[18px] py-3.5">
        {rows.map(([label, value]) => (
          <p key={label} className="text-sm">
            {label}: <span className="text-muted-foreground">{value}</span>
          </p>
        ))}
      </CardContent>
    </Card>
  );
}
