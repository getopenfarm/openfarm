import { WrenchIcon } from 'lucide-react';
import { toast } from 'sonner';
import { EmptyState } from '@/components/empty-state/EmptyState';
import { PageHeader } from '@/components/page-header/PageHeader';
import { SkeletonGrid } from '@/components/skeleton-grid/SkeletonGrid';
import { Button } from '@/components/ui/button';
import type { ListFleetResponse } from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsPrintersListFleetEndpoint } from '@/lib/api/generated/printers/printers';
import { NeedsAttentionCard } from './components/NeedsAttentionCard';
import { TaskTemplatesTable } from './components/TaskTemplatesTable';
import { MAINTENANCE_FLAG_SEEDS, TASK_TEMPLATES } from './data/mockMaintenance';
import { matchMaintenanceToFleet } from './lib/matchMaintenanceToFleet';

export function MaintenancePage() {
  const fleetQuery = useOpenFarmApiEndpointsPrintersListFleetEndpoint();
  const printers =
    (fleetQuery.data?.data as ListFleetResponse | undefined)?.printers ?? [];

  const flags = matchMaintenanceToFleet(printers, MAINTENANCE_FLAG_SEEDS);
  const overdueCount = flags.filter((f) => f.severity === 'overdue').length;
  const dueCount = flags.filter((f) => f.severity === 'due').length;

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title="Maintenance"
        description={
          <>
            <span className="font-medium text-kind-red">
              {overdueCount} overdue
            </span>
            {' · '}
            <span className="font-medium text-kind-amber">
              {dueCount} due soon
            </span>
          </>
        }
        action={
          <Button onClick={() => toast('Not yet implemented')}>New task</Button>
        }
      />

      <div className="flex flex-col gap-3">
        <h3 className="font-heading text-lg font-semibold">Needs attention</h3>
        {fleetQuery.isLoading ? (
          <SkeletonGrid itemClassName="h-32" />
        ) : printers.length === 0 ? (
          <EmptyState
            icon={<WrenchIcon className="size-8 text-muted-foreground" />}
            description="No printers registered yet. Add one on the Printers page to see maintenance flags here."
          />
        ) : (
          <div className="grid grid-cols-1 gap-4 [grid-template-columns:repeat(auto-fill,minmax(280px,1fr))] sm:grid-cols-2 xl:grid-cols-3">
            {flags.map((flag) => (
              <NeedsAttentionCard
                key={`${flag.printerId}-${flag.task}`}
                flag={flag}
              />
            ))}
          </div>
        )}
      </div>

      <div className="flex flex-col gap-3">
        <h3 className="font-heading text-lg font-semibold">Task templates</h3>
        <TaskTemplatesTable templates={TASK_TEMPLATES} />
      </div>
    </div>
  );
}
