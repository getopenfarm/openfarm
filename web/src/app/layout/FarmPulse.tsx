import { StatusBadge } from '@/components/status-badge/StatusBadge';
import { deriveFleetStatusCounts } from '@/features/printers/lib/fleetStatus';
import type { ListFleetResponse } from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsPrintersListFleetEndpoint } from '@/lib/api/generated/printers/printers';

export function FarmPulse() {
  const fleetQuery = useOpenFarmApiEndpointsPrintersListFleetEndpoint();

  if (!fleetQuery.data || fleetQuery.isError) return null;

  const printers =
    (fleetQuery.data.data as ListFleetResponse | undefined)?.printers ?? [];
  const counts = deriveFleetStatusCounts(printers);

  if (counts.total === 0) return null;

  return (
    <div className="hidden items-center gap-1.5 sm:flex">
      <StatusBadge kind="green" label={`${counts.printing} printing`} />
      <StatusBadge kind="amber" label={`${counts.awaitingClear} awaiting`} />
      <StatusBadge kind="zinc" label={`${counts.idle} idle`} />
    </div>
  );
}
