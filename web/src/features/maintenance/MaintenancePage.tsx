import { WrenchIcon } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
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
      <div className="flex items-center justify-between">
        <div>
          <h2 className="font-heading text-2xl font-semibold">Maintenance</h2>
          <p className="text-sm text-muted-foreground">
            <span className="font-medium text-kind-red">
              {overdueCount} overdue
            </span>
            {' · '}
            <span className="font-medium text-kind-amber">
              {dueCount} due soon
            </span>
          </p>
        </div>
        <Button onClick={() => toast('Not yet implemented')}>New task</Button>
      </div>

      <div className="flex flex-col gap-3">
        <h3 className="font-heading text-lg font-semibold">Needs attention</h3>
        {fleetQuery.isLoading ? (
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-3">
            {Array.from({ length: 3 }).map((_, index) => (
              // biome-ignore lint/suspicious/noArrayIndexKey: static skeleton placeholder cards
              <Skeleton key={`skeleton-${index}`} className="h-32 w-full" />
            ))}
          </div>
        ) : printers.length === 0 ? (
          <div className="flex flex-col items-center gap-2 rounded-xl border border-dashed p-12 text-center">
            <WrenchIcon className="size-8 text-muted-foreground" />
            <p className="text-sm text-muted-foreground">
              No printers registered yet. Add one on the Printers page to see
              maintenance flags here.
            </p>
          </div>
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
