import { useQueries } from '@tanstack/react-query';
import { formatSlicer } from '@/features/slicing/formatSlicer';
import { SlicerReadinessPanel } from '@/features/slicing/SlicerReadinessPanel';
import { getOpenFarmApiEndpointsPartsGetPartEndpointQueryOptions } from '@/lib/api/generated/parts/parts';

/**
 * A part is sliced only with the presets of the slicer it is pinned to. For the parts a run is about to queue, say
 * which printers and filaments cannot print with each of those slicers — before the jobs exist, not when they wait.
 * Creating the run is still allowed: the operator may fix the presets afterwards.
 */
export function RunSlicerReadiness({ partIds }: { partIds: string[] }) {
  const parts = useQueries({
    queries: partIds.map((id) =>
      getOpenFarmApiEndpointsPartsGetPartEndpointQueryOptions(id),
    ),
  });
  const slicers = new Map<string, { engine: string; version: string }>();
  for (const part of parts) {
    const slicer = part.data?.data?.slicer;
    if (slicer?.engine && slicer.version) {
      slicers.set(`${slicer.engine}::${slicer.version}`, {
        engine: slicer.engine,
        version: slicer.version,
      });
    }
  }
  if (slicers.size === 0) return null;

  return (
    <div className="flex flex-col gap-2">
      {[...slicers.values()].map((slicer) => (
        <div
          key={`${slicer.engine}::${slicer.version}`}
          className="flex flex-col gap-1"
        >
          <p className="font-medium text-sm">
            Parts pinned to {formatSlicer(slicer.engine, slicer.version)}
          </p>
          <SlicerReadinessPanel
            engine={slicer.engine}
            version={slicer.version}
          />
        </div>
      ))}
    </div>
  );
}
