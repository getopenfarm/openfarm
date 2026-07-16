import type { ColumnDef } from '@tanstack/react-table';
import { DataTable } from '@/components/data-table/DataTable';
import { DataTableColumnHeader } from '@/components/data-table/DataTableColumnHeader';
import { StatusBadge } from '@/components/status-badge/StatusBadge';
import { Swatch } from '@/components/swatch/Swatch';
import { Progress } from '@/components/ui/progress';
import type { Spool, SpoolState } from '../data/mockSpools';

const STATE_BADGE: Record<
  SpoolState,
  { kind: 'green' | 'blue' | 'zinc'; label: string }
> = {
  using: { kind: 'green', label: 'In use' },
  storage: { kind: 'blue', label: 'In storage' },
  finished: { kind: 'zinc', label: 'Finished' },
};

function remainingIndicatorClass(fraction: number) {
  if (fraction < 0.15) return 'bg-kind-red';
  if (fraction < 0.4) return 'bg-kind-amber';
  return 'bg-kind-green';
}

const columns: ColumnDef<Spool>[] = [
  {
    accessorKey: 'colorName',
    header: ({ column }) => (
      <DataTableColumnHeader column={column} title="Spool" />
    ),
    cell: ({ row }) => {
      const spool = row.original;
      return (
        <div className="flex items-center gap-2">
          <Swatch colorHex={spool.colorHex} size="md" />
          <div className="flex flex-col">
            <span className="font-medium">{spool.colorName}</span>
            <span className="text-xs text-muted-foreground">
              {spool.material}
            </span>
          </div>
        </div>
      );
    },
  },
  {
    accessorKey: 'vendor',
    header: ({ column }) => (
      <DataTableColumnHeader column={column} title="Vendor" />
    ),
  },
  {
    id: 'remaining',
    accessorFn: (spool) => spool.remainingGrams / spool.totalGrams,
    header: ({ column }) => (
      <DataTableColumnHeader column={column} title="Remaining" />
    ),
    cell: ({ row }) => {
      const spool = row.original;
      const fraction = spool.remainingGrams / spool.totalGrams;
      return (
        <div className="flex w-40 flex-col gap-1.5">
          <Progress
            value={fraction * 100}
            indicatorClassName={remainingIndicatorClass(fraction)}
          />
          <span className="text-xs text-muted-foreground">
            {spool.remainingGrams} / {spool.totalGrams} g
          </span>
        </div>
      );
    },
  },
  {
    accessorKey: 'state',
    header: ({ column }) => (
      <DataTableColumnHeader column={column} title="State" />
    ),
    cell: ({ row }) => {
      const meta = STATE_BADGE[row.original.state];
      return <StatusBadge kind={meta.kind} label={meta.label} />;
    },
  },
  {
    accessorKey: 'location',
    header: ({ column }) => (
      <DataTableColumnHeader column={column} title="Location" />
    ),
    cell: ({ row }) => (
      <span className="font-mono text-xs">{row.original.location ?? '—'}</span>
    ),
  },
];

interface SpoolsTableProps {
  spools: Spool[];
}

export function SpoolsTable({ spools }: SpoolsTableProps) {
  return (
    <DataTable
      columns={columns}
      data={spools}
      emptyState={<>No spools yet. Add one to track it here.</>}
    />
  );
}
