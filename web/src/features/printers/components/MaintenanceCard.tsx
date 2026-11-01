import { CheckIcon, PlusIcon } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { PermissionButton as Button } from '@/components/PermissionButton';
import {
  Card,
  CardAction,
  CardContent,
  CardHeader,
  CardTitle,
} from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { AddTasksDialog } from '@/features/maintenance/components/AddTasksDialog';
import {
  EnrollmentProgress,
  LastDone,
} from '@/features/maintenance/components/EnrollmentProgress';
import { MarkDoneDialog } from '@/features/maintenance/components/MarkDoneDialog';
import { useMaintenanceEnrollments } from '@/features/maintenance/maintenance';
import type { MaintenanceEnrollmentResponse } from '@/lib/api/generated/endpoints.schemas';
import { useAllowed } from '@/lib/auth/AccessContext';

/** This printer's maintenance, most due first, with Done per task. */
export function MaintenanceCard({ printerId }: { printerId: string }) {
  const canView = useAllowed('maintenance:view');
  const { query, items } = useMaintenanceEnrollments({ printerId });
  const [markDone, setMarkDone] = useState<MaintenanceEnrollmentResponse[]>([]);
  const [adding, setAdding] = useState(false);
  const addTasks = (
    <Button
      permission="maintenance:manage"
      variant="outline"
      size="sm"
      onClick={() => setAdding(true)}
    >
      <PlusIcon data-icon="inline-start" />
      Add tasks
    </Button>
  );
  if (!canView) return null;

  return (
    <Card className="gap-0 py-0">
      <CardHeader className="border-b px-[18px] py-3.5">
        <CardTitle className="text-sm font-semibold">
          <Link to="/maintenance" className="hover:underline">
            Maintenance
          </Link>
        </CardTitle>
        {items.length > 0 && <CardAction>{addTasks}</CardAction>}
      </CardHeader>
      <CardContent className="p-0">
        {query.isLoading ? (
          <Skeleton className="m-[18px] h-16" />
        ) : query.isError && !query.data ? (
          <ErrorState
            size="sm"
            bordered={false}
            title="Could not load maintenance"
            onRetry={() => query.refetch()}
          />
        ) : items.length === 0 ? (
          <EmptyState
            size="sm"
            bordered={false}
            title="No maintenance tracked"
            action={addTasks}
          />
        ) : (
          <ul>
            {items.map((e) => (
              <li
                key={e.id}
                className="flex items-center gap-3 border-b px-[18px] py-3 last:border-b-0"
              >
                <div className="flex min-w-0 flex-1 flex-col gap-1.5">
                  <div className="flex items-baseline justify-between gap-2">
                    <span className="truncate text-[13px] font-medium">
                      {e.taskName}
                    </span>
                    <LastDone enrollment={e} />
                  </div>
                  <EnrollmentProgress enrollment={e} />
                </div>
                <Button
                  permission="maintenance:complete"
                  type="button"
                  variant="outline"
                  size="sm"
                  aria-label={`Mark ${e.taskName} done`}
                  onClick={() => setMarkDone([e])}
                >
                  <CheckIcon data-icon="inline-start" />
                  Done
                </Button>
              </li>
            ))}
          </ul>
        )}
      </CardContent>
      <AddTasksDialog
        printerId={printerId}
        open={adding}
        onOpenChange={setAdding}
      />
      <MarkDoneDialog
        targets={markDone}
        onOpenChange={(open) => {
          if (!open) setMarkDone([]);
        }}
      />
    </Card>
  );
}
