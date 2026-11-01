import { useQueryClient } from '@tanstack/react-query';
import type { ColumnDef, RowSelectionState } from '@tanstack/react-table';
import { CheckIcon } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router';
import { toast } from 'sonner';
import { ConfirmDialog } from '@/components/confirm-dialog/ConfirmDialog';
import { DataTable } from '@/components/data-table/DataTable';
import { DataTableColumnHeader } from '@/components/data-table/DataTableColumnHeader';
import { DataTableRowActions } from '@/components/data-table/DataTableRowActions';
import { DataTableSelectionBar } from '@/components/data-table/DataTableSelectionBar';
import { PermissionButton as Button } from '@/components/PermissionButton';
import { PermissionMenuItem as DropdownMenuItem } from '@/components/PermissionMenuItem';
import { Checkbox } from '@/components/ui/checkbox';
import { DropdownMenuGroup } from '@/components/ui/dropdown-menu';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import type { MaintenanceEnrollmentResponse } from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsMaintenanceUnenrollPrinterEndpoint } from '@/lib/api/generated/maintenance/maintenance';
import { invalidateMaintenanceQueries } from '../maintenance';
import { EnrollmentProgress, LastDone } from './EnrollmentProgress';
import { MarkDoneDialog } from './MarkDoneDialog';
import { SetLastDoneDialog } from './SetLastDoneDialog';

type Row = MaintenanceEnrollmentResponse;
type Open = (row: Row) => void;

const makeColumns = (
  markDone: Open,
  setLastDone: Open,
  unenroll: Open,
): ColumnDef<Row>[] => [
  {
    id: 'select',
    enableSorting: false,
    enableHiding: false,
    header: ({ table }) => (
      <Checkbox
        aria-label="Select all on this page"
        checked={
          table.getIsAllPageRowsSelected() ||
          (table.getIsSomePageRowsSelected() && 'indeterminate')
        }
        onCheckedChange={(checked) =>
          table.toggleAllPageRowsSelected(checked === true)
        }
      />
    ),
    cell: ({ row }) => (
      <Checkbox
        aria-label={`Select ${row.original.taskName} on ${row.original.printerName}`}
        checked={row.getIsSelected()}
        onCheckedChange={(checked) => row.toggleSelected(checked === true)}
      />
    ),
  },
  {
    id: 'printer',
    accessorFn: (e) => e.printerName ?? '',
    meta: { label: 'Printer' },
    header: ({ column }) => (
      <DataTableColumnHeader column={column} title="Printer" />
    ),
    cell: ({ row }) => (
      <Link
        to={`/printers/${row.original.printerId}`}
        className="font-medium hover:underline"
      >
        {row.original.printerName}
      </Link>
    ),
  },
  {
    id: 'task',
    accessorFn: (e) => e.taskName ?? '',
    meta: { label: 'Task' },
    header: ({ column }) => (
      <DataTableColumnHeader column={column} title="Task" />
    ),
  },
  {
    id: 'progress',
    accessorFn: (e) => (e.interval ? (e.used ?? 0) / e.interval : 0),
    meta: { label: 'Progress' },
    header: ({ column }) => (
      <DataTableColumnHeader column={column} title="Progress" />
    ),
    cell: ({ row }) => <EnrollmentProgress enrollment={row.original} />,
  },
  {
    id: 'lastDone',
    accessorFn: (e) => e.cycleStartedAtUtc ?? '',
    meta: { label: 'Last done' },
    header: ({ column }) => (
      <DataTableColumnHeader column={column} title="Last done" />
    ),
    cell: ({ row }) => <LastDone enrollment={row.original} />,
  },
  {
    id: 'done',
    enableSorting: false,
    enableHiding: false,
    header: () => <span className="sr-only">Done</span>,
    cell: ({ row }) => (
      <Button
        permission="maintenance:complete"
        type="button"
        variant="outline"
        size="sm"
        aria-label={`Mark ${row.original.taskName} on ${row.original.printerName} done`}
        onClick={() => markDone(row.original)}
      >
        <CheckIcon data-icon="inline-start" />
        Done
      </Button>
    ),
  },
  {
    id: 'actions',
    enableSorting: false,
    enableHiding: false,
    header: () => <span className="sr-only">Actions</span>,
    cell: ({ row }) => (
      <DataTableRowActions
        label={`${row.original.taskName} on ${row.original.printerName}`}
        permission="maintenance:complete|maintenance:manage"
      >
        <DropdownMenuGroup>
          <DropdownMenuItem
            permission="maintenance:complete"
            onSelect={() => markDone(row.original)}
          >
            Mark done
          </DropdownMenuItem>
          <DropdownMenuItem
            permission="maintenance:manage"
            onSelect={() => setLastDone(row.original)}
          >
            Set last done
          </DropdownMenuItem>
          <DropdownMenuItem
            permission="maintenance:manage"
            variant="destructive"
            onSelect={() => unenroll(row.original)}
          >
            Unenroll
          </DropdownMenuItem>
        </DropdownMenuGroup>
      </DataTableRowActions>
    ),
  },
];

/** Enrollments with due progress; Done per row, bulk Mark done on the selection. */
export function EnrollmentsTable({ enrollments }: { enrollments: Row[] }) {
  const queryClient = useQueryClient();
  const [selection, setSelection] = useState<RowSelectionState>({});
  const [markDone, setMarkDone] = useState<Row[]>([]);
  const [lastDone, setLastDone] = useState<Row | null>(null);
  const [unenroll, setUnenroll] = useState<Row | null>(null);
  const columns = useMemo(
    () => makeColumns((row) => setMarkDone([row]), setLastDone, setUnenroll),
    [],
  );
  // Selection follows the rows on screen: a filter or refetch drops rows that left.
  useEffect(() => {
    const ids = new Set(enrollments.map((e) => e.id));
    setSelection((previous) => {
      const kept = Object.entries(previous).filter(([id]) => ids.has(id));
      return kept.length === Object.keys(previous).length
        ? previous
        : Object.fromEntries(kept);
    });
  }, [enrollments]);
  const selected = enrollments.filter((e) => e.id && selection[e.id]);

  const remove = useOpenFarmApiEndpointsMaintenanceUnenrollPrinterEndpoint({
    mutation: {
      onSuccess: () => {
        toast.success('Printer unenrolled.');
        invalidateMaintenanceQueries(queryClient);
        setUnenroll(null);
      },
      onError: async (error) =>
        toast.error(await extractErrorDetail(error, 'Could not unenroll.')),
    },
  });

  return (
    <>
      <DataTable
        pageSize={25}
        columns={columns}
        data={enrollments}
        getRowId={(e, index) => e.id ?? `missing-${index}`}
        rowSelection={selection}
        onRowSelectionChange={setSelection}
        enableRowSelection
        toolbar={
          <DataTableSelectionBar
            selectedCount={selected.length}
            label="Maintenance actions"
            onClear={() => setSelection({})}
          >
            <Button
              permission="maintenance:complete"
              type="button"
              size="sm"
              onClick={() => setMarkDone(selected)}
            >
              <CheckIcon data-icon="inline-start" />
              Mark done
            </Button>
          </DataTableSelectionBar>
        }
      />
      <MarkDoneDialog
        targets={markDone}
        onOpenChange={(open) => {
          if (!open) setMarkDone([]);
        }}
        onDone={() => setSelection({})}
      />
      <SetLastDoneDialog
        target={lastDone}
        onOpenChange={(open) => {
          if (!open) setLastDone(null);
        }}
      />
      <ConfirmDialog
        open={!!unenroll}
        onOpenChange={(open) => {
          if (!open) setUnenroll(null);
        }}
        title="Unenroll printer?"
        description={`${unenroll?.taskName} · ${unenroll?.printerName}. Cycle progress is lost; logs stay.`}
        confirmLabel={remove.isPending ? 'Unenrolling…' : 'Unenroll'}
        destructive
        closeOnConfirm={false}
        isPending={remove.isPending}
        onConfirm={() =>
          remove.mutate({
            taskId: unenroll?.taskId ?? '',
            printerId: unenroll?.printerId ?? '',
          })
        }
      />
    </>
  );
}
