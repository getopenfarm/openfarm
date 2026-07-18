import { PageHeader } from '@/components/page-header/PageHeader';
import { MAINTENANCE_FLAG_SEEDS } from '@/features/maintenance/data/mockMaintenance';
import { matchMaintenanceToFleet } from '@/features/maintenance/lib/matchMaintenanceToFleet';
import { deriveFleetStatusCounts } from '@/features/printers/lib/fleetStatus';
import type { ListFleetResponse } from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsPrintersListFleetEndpoint } from '@/lib/api/generated/printers/printers';
import { CurrentlyPrintingPanel } from './components/CurrentlyPrintingPanel';
import { KpiRow } from './components/KpiRow';
import { LowFilamentPanel } from './components/LowFilamentPanel';
import { MaintenanceDuePanel } from './components/MaintenanceDuePanel';

export function DashboardPage() {
  const fleetQuery = useOpenFarmApiEndpointsPrintersListFleetEndpoint();
  const printers =
    (fleetQuery.data?.data as ListFleetResponse | undefined)?.printers ?? [];

  const counts = deriveFleetStatusCounts(printers);
  // Mock — no Maintenance backend yet, but printer identity is real (see
  // @/features/maintenance/lib/matchMaintenanceToFleet).
  const maintenanceFlags = matchMaintenanceToFleet(
    printers,
    MAINTENANCE_FLAG_SEEDS,
  );

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title="Dashboard"
        description={`Live overview of ${counts.total} printers across your fleet.`}
      />

      <KpiRow counts={counts} maintenanceFlags={maintenanceFlags} />

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-[1.7fr_1fr]">
        <CurrentlyPrintingPanel
          printers={printers}
          isLoading={fleetQuery.isLoading}
        />
        <div className="flex flex-col gap-4">
          <LowFilamentPanel />
          <MaintenanceDuePanel
            flags={maintenanceFlags}
            isLoading={fleetQuery.isLoading}
          />
        </div>
      </div>
    </div>
  );
}
