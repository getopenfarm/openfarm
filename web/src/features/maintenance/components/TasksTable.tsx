import { useQueryClient } from '@tanstack/react-query';
import type { ColumnDef } from '@tanstack/react-table';
import { Trash2Icon } from 'lucide-react';
import { useMemo, useState } from 'react';
import { Link, useNavigate } from 'react-router';
import { toast } from 'sonner';
import { ConfirmDialog } from '@/components/confirm-dialog/ConfirmDialog';
import { DataTable } from '@/components/data-table/DataTable';
import { DataTableColumnHeader } from '@/components/data-table/DataTableColumnHeader';
import { DataTableRowActions } from '@/components/data-table/DataTableRowActions';
import { PermissionMenuItem as DropdownMenuItem } from '@/components/PermissionMenuItem';
import { StatusBadge } from '@/components/status-badge/StatusBadge';
import { DropdownMenuGroup } from '@/components/ui/dropdown-menu';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import type { MaintenanceTaskResponse } from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsMaintenanceDeleteMaintenanceTaskEndpoint } from '@/lib/api/generated/maintenance/maintenance';
import { useAllowed } from '@/lib/auth/AccessContext';
import { pluralize } from '@/lib/pluralize';
import {
  intervalsLabel,
  invalidateMaintenanceQueries,
  STATE_BADGE,
} from '../maintenance';

type Row = MaintenanceTaskResponse;

const makeColumns = (
  remove: (row: Row) => void,
  canEdit: boolean,
): ColumnDef<Row>[] => [
  {
    id: 'name',
    accessorFn: (t) => t.name ?? '',
    meta: { label: 'Task' },
    header: ({ column }) => (
      <DataTableColumnHeader column={column} title="Task" />
    ),
    cell: ({ row }) =>
      canEdit ? (
        <Link
          to={`/maintenance/${row.original.id}/edit`}
          className="font-medium hover:underline"
        >
          {row.original.name}
        </Link>
      ) : (
        <span className="font-medium">{row.original.name}</span>
      ),
  },
  {
    id: 'every',
    header: 'Every',
    meta: { label: 'Every' },
    cell: ({ row }) => (
      <span className="text-sm tabular-nums">
        {intervalsLabel(row.original)}
      </span>
    ),
  },
  {
    id: 'warn',
    header: 'Warn at',
    meta: { label: 'Warn at' },
    cell: ({ row }) => (
      <span className="tabular-nums">{row.original.warnAtPercent}%</span>
    ),
  },
  {
    id: 'printers',
    accessorFn: (t) => t.printerIds?.length ?? 0,
    meta: { label: 'Printers' },
    header: ({ column }) => (
      <DataTableColumnHeader column={column} title="Printers" />
    ),
    cell: ({ row }) =>
      pluralize(row.original.printerIds?.length ?? 0, 'printer'),
  },
  {
    id: 'state',
    header: 'State',
    meta: { label: 'State' },
    cell: ({ row }) => {
      const badge = STATE_BADGE[row.original.worstState ?? ''];
      return badge ? (
        <StatusBadge kind={badge.kind} label={badge.label} />
      ) : (
        <span className="text-muted-foreground">—</span>
      );
    },
  },
  {
    id: 'actions',
    enableSorting: false,
    enableHiding: false,
    header: () => <span className="sr-only">Actions</span>,
    cell: ({ row }) => (
      <DataTableRowActions
        label={row.original.name ?? 'task'}
        permission="maintenance:manage|maintenance:delete"
      >
        <DropdownMenuGroup>
          <DropdownMenuItem permission="maintenance:manage" asChild>
            <Link to={`/maintenance/${row.original.id}/edit`}>Edit</Link>
          </DropdownMenuItem>
          <DropdownMenuItem
            permission="maintenance:delete"
            variant="destructive"
            onSelect={() => remove(row.original)}
          >
            <Trash2Icon /> Delete
          </DropdownMenuItem>
        </DropdownMenuGroup>
      </DataTableRowActions>
    ),
  },
];

/** Task definitions; editing one changes every enrolled printer. */
export function TasksTable({ tasks }: { tasks: Row[] }) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const canEdit = useAllowed('maintenance:manage');
  const [target, setTarget] = useState<Row | null>(null);
  const columns = useMemo(() => makeColumns(setTarget, canEdit), [canEdit]);
  const remove =
    useOpenFarmApiEndpointsMaintenanceDeleteMaintenanceTaskEndpoint({
      mutation: {
        onSuccess: () => {
          toast.success('Task deleted.');
          invalidateMaintenanceQueries(queryClient);
          setTarget(null);
        },
        onError: async (error) =>
          toast.error(
            await extractErrorDetail(error, 'Could not delete task.'),
          ),
      },
    });
  return (
    <>
      <DataTable
        pageSize={25}
        columns={columns}
        data={tasks}
        onRowClick={
          canEdit ? (t) => navigate(`/maintenance/${t.id}/edit`) : undefined
        }
      />
      <ConfirmDialog
        open={!!target}
        onOpenChange={(open) => {
          if (!open) setTarget(null);
        }}
        title="Delete task?"
        description={`${target?.name}. Enrollments are removed; logs stay.`}
        confirmLabel={remove.isPending ? 'Deleting…' : 'Delete'}
        destructive
        closeOnConfirm={false}
        isPending={remove.isPending}
        onConfirm={() => remove.mutate({ id: target?.id ?? '' })}
      />
    </>
  );
}
