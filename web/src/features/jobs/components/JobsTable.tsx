import type { ColumnDef } from '@tanstack/react-table';
import { CheckerboardThumbnail } from '@/components/checkerboard-thumbnail/CheckerboardThumbnail';
import { DataTable } from '@/components/data-table/DataTable';
import { DataTableColumnHeader } from '@/components/data-table/DataTableColumnHeader';
import type { BadgeKind } from '@/components/status-badge/kinds';
import { StatusBadge } from '@/components/status-badge/StatusBadge';
import { Swatch } from '@/components/swatch/Swatch';
import { Progress } from '@/components/ui/progress';
import type { MockJob } from '../data/mockJobsStore';

const PRIORITY_KIND: Record<MockJob['priority'], BadgeKind> = {
  High: 'red',
  Normal: 'zinc',
  Low: 'blue',
};

const STATUS_KIND: Record<MockJob['status'], BadgeKind> = {
  printing: 'green',
  queued: 'zinc',
  paused: 'amber',
  completed: 'blue',
  failed: 'red',
};

const STATUS_LABEL: Record<MockJob['status'], string> = {
  printing: 'Printing',
  queued: 'Queued',
  paused: 'Paused',
  completed: 'Completed',
  failed: 'Failed',
};

function zeroPad(index: number) {
  return String(index + 1).padStart(2, '0');
}

const columns: ColumnDef<MockJob>[] = [
  {
    id: 'index',
    header: '#',
    cell: ({ row }) => (
      <span className="font-mono text-xs text-muted-foreground">
        {zeroPad(row.index)}
      </span>
    ),
  },
  {
    id: 'job',
    accessorKey: 'name',
    header: ({ column }) => (
      <DataTableColumnHeader column={column} title="Job" />
    ),
    cell: ({ row }) => (
      <div className="flex items-center gap-3">
        <CheckerboardThumbnail className="size-9 rounded-md" />
        <div className="flex flex-col">
          <span className="font-medium">{row.original.name}</span>
          {row.original.partName ? (
            <span className="text-xs text-muted-foreground">
              {row.original.partName}
            </span>
          ) : null}
        </div>
      </div>
    ),
  },
  {
    id: 'priority',
    accessorKey: 'priority',
    header: ({ column }) => (
      <DataTableColumnHeader column={column} title="Priority" />
    ),
    cell: ({ row }) => (
      <StatusBadge
        kind={PRIORITY_KIND[row.original.priority]}
        label={row.original.priority}
      />
    ),
  },
  {
    id: 'printer',
    accessorKey: 'printerName',
    header: ({ column }) => (
      <DataTableColumnHeader column={column} title="Printer" />
    ),
    cell: ({ row }) =>
      row.original.printerName ?? (
        <span className="text-muted-foreground">—</span>
      ),
  },
  {
    id: 'filament',
    header: 'Filament',
    cell: ({ row }) =>
      row.original.filamentMaterial ? (
        <div className="flex items-center gap-2">
          <Swatch colorHex={row.original.filamentColorHex} size="sm" />
          <span>{row.original.filamentMaterial}</span>
        </div>
      ) : (
        <span className="text-muted-foreground">—</span>
      ),
  },
  {
    id: 'status',
    accessorKey: 'status',
    header: ({ column }) => (
      <DataTableColumnHeader column={column} title="Status" />
    ),
    cell: ({ row }) => (
      <StatusBadge
        kind={STATUS_KIND[row.original.status]}
        label={STATUS_LABEL[row.original.status]}
      />
    ),
  },
  {
    id: 'progress',
    header: 'Progress',
    cell: ({ row }) => {
      const { status, progressPercent } = row.original;
      if (
        (status !== 'printing' && status !== 'paused') ||
        progressPercent == null
      ) {
        return <span className="text-muted-foreground">—</span>;
      }
      return (
        <div className="flex items-center gap-2">
          <Progress value={progressPercent} className="w-24" />
          <span className="text-xs text-muted-foreground">
            {progressPercent}%
          </span>
        </div>
      );
    },
  },
];

interface JobsTableProps {
  jobs: MockJob[];
}

export function JobsTable({ jobs }: JobsTableProps) {
  return <DataTable columns={columns} data={jobs} />;
}
