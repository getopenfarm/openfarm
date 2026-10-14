import { ClipboardListIcon, PlusIcon, WrenchIcon } from 'lucide-react';
import { toast } from 'sonner';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { PageHeader } from '@/components/page-header/PageHeader';
import { SkeletonGrid } from '@/components/skeleton-grid/SkeletonGrid';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
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
          <Button onClick={() => toast('Not yet implemented')}>
            <PlusIcon data-icon="inline-start" />
            Add task
          </Button>
        }
      />

      <Card className="shrink-0">
        <CardHeader>
          <CardTitle>Needs attention</CardTitle>
        </CardHeader>
        <CardContent>
          {fleetQuery.isLoading ? (
            <SkeletonGrid itemClassName="h-32" />
          ) : fleetQuery.isError ? (
            <ErrorState
              size="sm"
              title="Could not load printers"
              onRetry={() => fleetQuery.refetch()}
            />
          ) : printers.length === 0 ? (
            <EmptyState
              size="sm"
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
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Task templates</CardTitle>
        </CardHeader>
        <CardContent>
          {TASK_TEMPLATES.length === 0 ? (
            <EmptyState
              size="sm"
              icon={
                <ClipboardListIcon className="size-8 text-muted-foreground" />
              }
              title="No task templates yet"
              description="Task templates define what maintenance to track and when."
              action={
                <Button onClick={() => toast('Not yet implemented')}>
                  <PlusIcon data-icon="inline-start" />
                  Add task
                </Button>
              }
            />
          ) : (
            <TaskTemplatesTable templates={TASK_TEMPLATES} />
          )}
        </CardContent>
      </Card>
    </div>
  );
}
