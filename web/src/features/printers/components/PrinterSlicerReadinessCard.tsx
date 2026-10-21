import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { formatSlicer } from '@/features/slicing/formatSlicer';
import { SlicerReadinessPanel } from '@/features/slicing/SlicerReadinessPanel';
import { useOpenFarmApiEndpointsSettingsGetSlicingDefaultsEndpoint } from '@/lib/api/generated/settings/settings';

/**
 * Whether this printer's machine and process presets (and the filament assignments that apply to it) exist in each
 * bundled slicer's own catalog. A part is sliced only with its own slicer's presets, so a printer can be ready for
 * one slicer and blocked for another.
 */
export function PrinterSlicerReadinessCard({
  printerId,
}: {
  printerId: string;
}) {
  const query = useOpenFarmApiEndpointsSettingsGetSlicingDefaultsEndpoint();
  const slicers = query.data?.data?.available ?? [];
  if (slicers.length === 0) return null;

  return (
    <Card className="gap-0 py-0">
      <CardHeader className="border-b px-[18px] py-3.5">
        <CardTitle className="text-sm font-semibold">
          Slicer readiness
        </CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-3 px-[18px] py-3.5">
        {slicers.map((slicer) => (
          <div
            key={`${slicer.engine}::${slicer.version}`}
            className="flex flex-col gap-1"
          >
            <p className="font-medium text-sm">
              {formatSlicer(slicer.engine, slicer.version)}
            </p>
            <SlicerReadinessPanel
              engine={slicer.engine ?? ''}
              version={slicer.version ?? ''}
              printerId={printerId}
            />
          </div>
        ))}
      </CardContent>
    </Card>
  );
}
