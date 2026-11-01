import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Link } from 'react-router';
import { toast } from 'sonner';
import { ErrorState } from '@/components/empty-state/EmptyState';
import { PermissionButton as Button } from '@/components/PermissionButton';
import { Checkbox } from '@/components/ui/checkbox';
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Skeleton } from '@/components/ui/skeleton';
import { Spinner } from '@/components/ui/spinner';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import type {
  MaintenanceChangeResponse,
  MaintenanceTaskResponse,
  MaintenanceTasksResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsMaintenanceListMaintenanceTasksEndpoint } from '@/lib/api/generated/maintenance/maintenance';
import { useOpenFarmApiEndpointsMaintenanceEnrollPrinterInTasksEndpoint } from '@/lib/api/generated/printers/printers';
import { pluralize } from '@/lib/pluralize';
import { intervalsLabel, invalidateMaintenanceQueries } from '../maintenance';

/** Enrolls one printer in existing tasks (templates). */
export function AddTasksDialog({
  printerId,
  open,
  onOpenChange,
}: {
  printerId: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const query = useOpenFarmApiEndpointsMaintenanceListMaintenanceTasksEndpoint({
    // Fresh on every open, so tasks enrolled elsewhere are not offered again.
    query: { enabled: open, refetchOnMount: 'always' },
  });
  const tasks = (query.data?.data as MaintenanceTasksResponse | undefined)
    ?.items;

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Add tasks</DialogTitle>
          <DialogDescription>
            Cycle starts now.{' '}
            <Link to="/maintenance/new" className="underline">
              New task
            </Link>
          </DialogDescription>
        </DialogHeader>
        {query.isError && !query.isFetching ? (
          <ErrorState
            size="sm"
            bordered={false}
            title="Could not load tasks"
            onRetry={() => query.refetch()}
          />
        ) : tasks && !query.isFetching ? (
          // Mounted per open (the dialog unmounts its content), so the selection resets each time.
          <Checklist
            printerId={printerId}
            noTasks={tasks.length === 0}
            available={tasks.filter((t) => !t.printerIds?.includes(printerId))}
            onDone={() => onOpenChange(false)}
          />
        ) : (
          <Skeleton className="h-24" />
        )}
      </DialogContent>
    </Dialog>
  );
}

function Checklist({
  printerId,
  noTasks,
  available,
  onDone,
}: {
  printerId: string;
  noTasks: boolean;
  available: MaintenanceTaskResponse[];
  onDone: () => void;
}) {
  const queryClient = useQueryClient();
  const shown = available.flatMap((t) =>
    t.id ? [{ ...t, id: t.id, name: t.name ?? '' }] : [],
  );
  const [checked, setChecked] = useState(() => new Set<string>());
  const enroll = useOpenFarmApiEndpointsMaintenanceEnrollPrinterInTasksEndpoint(
    {
      mutation: {
        onSuccess: (response) => {
          const added =
            (response.data as MaintenanceChangeResponse | undefined)?.count ??
            0;
          toast.success(`${pluralize(added, 'task')} added.`);
          invalidateMaintenanceQueries(queryClient);
          onDone();
        },
        onError: async (error) =>
          toast.error(await extractErrorDetail(error, 'Could not add tasks.')),
      },
    },
  );
  const selected = shown.filter((t) => checked.has(t.id));

  const toggle = (id: string, on: boolean) =>
    setChecked((prev) => {
      const next = new Set(prev);
      if (on) next.add(id);
      else next.delete(id);
      return next;
    });

  return (
    <>
      {shown.length === 0 ? (
        <p className="text-sm text-muted-foreground">
          {noTasks ? 'No tasks yet.' : 'Already in every task.'}
        </p>
      ) : (
        <>
          <div className="flex gap-3 text-xs">
            <button
              type="button"
              className="text-muted-foreground hover:underline"
              onClick={() => setChecked(new Set(shown.map((t) => t.id)))}
            >
              Select all
            </button>
            <button
              type="button"
              className="text-muted-foreground hover:underline"
              onClick={() => setChecked(new Set())}
            >
              None
            </button>
          </div>
          <ul className="-mx-1 max-h-80 overflow-y-auto">
            {shown.map((t) => (
              <li key={t.id}>
                <label
                  htmlFor={`add-task-${t.id}`}
                  className="flex cursor-pointer items-center gap-3 rounded-md px-1 py-2 hover:bg-muted/50"
                >
                  <Checkbox
                    id={`add-task-${t.id}`}
                    checked={checked.has(t.id)}
                    onCheckedChange={(v) => toggle(t.id, v === true)}
                  />
                  <span className="min-w-0 flex-1">
                    <span className="block truncate text-[13px] font-medium">
                      {t.name}
                    </span>
                    <span className="block text-xs text-muted-foreground">
                      {intervalsLabel(t)}
                    </span>
                  </span>
                </label>
              </li>
            ))}
          </ul>
        </>
      )}
      <DialogFooter>
        <DialogClose asChild>
          <Button type="button" variant="outline">
            Cancel
          </Button>
        </DialogClose>
        <Button
          permission="maintenance:manage"
          disabled={selected.length === 0 || enroll.isPending}
          onClick={() =>
            enroll.mutate({
              id: printerId,
              data: { taskIds: selected.map((t) => t.id) },
            })
          }
        >
          {enroll.isPending && <Spinner data-icon="inline-start" />}
          Add{selected.length ? ` ${selected.length}` : ''}
        </Button>
      </DialogFooter>
    </>
  );
}
