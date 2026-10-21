import { formatSlicer } from '@/features/slicing/formatSlicer';
import type { EstimationDetails } from '@/lib/api/generated/endpoints.schemas';

export function PartEstimationSource({
  estimation,
}: {
  estimation?: EstimationDetails | null;
}) {
  if (!estimation) return null;
  const source =
    estimation.source === 'DefaultProfiles'
      ? 'Default estimation profiles'
      : estimation.source === 'EmbeddedProject'
        ? 'Embedded project settings'
        : 'Embedded sliced metadata';
  const profiles = [
    estimation.machineModel,
    estimation.machineProfileName,
    estimation.processProfileName,
    ...(estimation.filamentProfileNames ?? []),
    estimation.buildPlate,
  ].filter(Boolean);
  return (
    <div className="flex flex-col gap-1 text-sm">
      <p>Estimate basis: {source}</p>
      {profiles.length > 0 && (
        <p className="text-muted-foreground">{profiles.join(' · ')}</p>
      )}
      {estimation.engine && (
        <p className="text-muted-foreground">
          {formatSlicer(estimation.engine, estimation.version)}
        </p>
      )}
      {estimation.source !== 'EmbeddedSlicedMetadata' && (
        <p className="text-muted-foreground">
          Production slicing uses the assigned printer’s settings and may
          produce different estimates.
        </p>
      )}
    </div>
  );
}
