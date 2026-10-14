import type { ColumnDef } from '@tanstack/react-table';
import { DataTable } from '@/components/data-table/DataTable';
import { DataTableColumnHeader } from '@/components/data-table/DataTableColumnHeader';
import type { BadgeKind } from '@/components/status-badge/kinds';
import { StatusBadge } from '@/components/status-badge/StatusBadge';
import type {
  TaskTemplate,
  TaskTemplateDueKind,
} from '../data/mockMaintenance';

const DUE_KIND_TO_BADGE: Record<
  TaskTemplateDueKind,
  { kind: BadgeKind; label: string }
> = {
  overdue: { kind: 'red', label: 'Overdue' },
  due: { kind: 'amber', label: 'Due soon' },
  ok: { kind: 'zinc', label: 'OK' },
};

const columns: ColumnDef<TaskTemplate>[] = [
  {
    accessorKey: 'task',
    header: ({ column }) => (
      <DataTableColumnHeader column={column} title="Task" />
    ),
  },
  {
    accessorKey: 'trigger',
    header: ({ column }) => (
      <DataTableColumnHeader column={column} title="Trigger" />
    ),
  },
  {
    accessorKey: 'appliesTo',
    header: ({ column }) => (
      <DataTableColumnHeader column={column} title="Applies to" />
    ),
  },
  {
    accessorKey: 'lastPerformed',
    header: ({ column }) => (
      <DataTableColumnHeader column={column} title="Last performed" />
    ),
  },
  {
    id: 'nextDue',
    accessorFn: (template) => template.nextDueKind,
    header: ({ column }) => (
      <DataTableColumnHeader column={column} title="Next due" />
    ),
    cell: ({ row }) => {
      const { kind, label } = DUE_KIND_TO_BADGE[row.original.nextDueKind];
      return (
        <div className="flex items-center gap-2">
          <StatusBadge kind={kind} label={label} />
          <span className="text-sm text-muted-foreground">
            {row.original.nextDueText}
          </span>
        </div>
      );
    },
  },
];

interface TaskTemplatesTableProps {
  templates: TaskTemplate[];
}

export function TaskTemplatesTable({ templates }: TaskTemplatesTableProps) {
  return <DataTable pageSize={25} columns={columns} data={templates} />;
}
