import { KpiCard } from '@/components/kpi-card/KpiCard';
import type { MatchedMaintenanceFlag } from '@/features/maintenance/lib/matchMaintenanceToFleet';
import type { FleetStatusCounts } from '@/features/printers/lib/fleetStatus';
import { LOW_FILAMENT_SPOOLS } from '../data/mockDashboardFilament';

interface KpiRowProps {
  /** Real — derived from the live fleet via `deriveFleetStatusCounts`. */
  counts: FleetStatusCounts;
  /** Mock — cross-referenced onto real printer names; see matchMaintenanceToFleet. */
  maintenanceFlags: MatchedMaintenanceFlag[];
}

export function KpiRow({ counts, maintenanceFlags }: KpiRowProps) {
  const overdueCount = maintenanceFlags.filter(
    (flag) => flag.severity === 'overdue',
  ).length;
  const dueCount = maintenanceFlags.filter(
    (flag) => flag.severity === 'due',
  ).length;

  return (
    <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
      <KpiCard
        label="Printing now"
        value={counts.printing}
        sub={`of ${counts.total} printers`}
        dotClassName="bg-status-printing"
      />
      <KpiCard
        label="Awaiting clear"
        value={counts.awaitingClear}
        sub="beds need clearing"
        dotClassName="bg-status-attention"
      />
      <KpiCard
        label="Low filament"
        value={LOW_FILAMENT_SPOOLS.length}
        sub="spools under 25%"
        dotClassName="bg-kind-orange"
      />
      <KpiCard
        label="Maintenance due"
        value={maintenanceFlags.length}
        sub={`${overdueCount} overdue · ${dueCount} soon`}
        dotClassName="bg-kind-violet"
      />
    </div>
  );
}
