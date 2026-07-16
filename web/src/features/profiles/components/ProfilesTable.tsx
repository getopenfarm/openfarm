import type { ColumnDef } from '@tanstack/react-table';
import { DataTable } from '@/components/data-table/DataTable';
import { DataTableColumnHeader } from '@/components/data-table/DataTableColumnHeader';
import { StatusBadge } from '@/components/status-badge/StatusBadge';
import type { Profile } from '../data/mockProfiles';

const EM_DASH = '—';

const columns: ColumnDef<Profile>[] = [
  {
    accessorKey: 'name',
    header: ({ column }) => (
      <DataTableColumnHeader column={column} title="Name" />
    ),
  },
  {
    accessorKey: 'category',
    header: ({ column }) => (
      <DataTableColumnHeader column={column} title="Category" />
    ),
    cell: ({ row }) => {
      const category = row.original.category;
      return category === 'Machine' ? (
        <StatusBadge kind="violet" label="Machine" />
      ) : (
        <StatusBadge kind="green" label="Filament" />
      );
    },
  },
  {
    accessorKey: 'nozzleSizeMm',
    header: ({ column }) => (
      <DataTableColumnHeader column={column} title="Nozzle size" />
    ),
    cell: ({ row }) => `${row.original.nozzleSizeMm}mm`,
  },
  {
    id: 'nozzleTempC',
    accessorFn: (profile) => profile.nozzleTempC ?? null,
    header: ({ column }) => (
      <DataTableColumnHeader column={column} title="Nozzle temp" />
    ),
    cell: ({ row }) => {
      const value = row.original.nozzleTempC;
      return value === undefined ? EM_DASH : `${value}°C`;
    },
  },
  {
    id: 'bedTempC',
    accessorFn: (profile) => profile.bedTempC ?? null,
    header: ({ column }) => (
      <DataTableColumnHeader column={column} title="Bed temp" />
    ),
    cell: ({ row }) => {
      const value = row.original.bedTempC;
      return value === undefined ? EM_DASH : `${value}°C`;
    },
  },
  {
    id: 'maxVolumetricSpeedMm3s',
    accessorFn: (profile) => profile.maxVolumetricSpeedMm3s ?? null,
    header: ({ column }) => (
      <DataTableColumnHeader column={column} title="Max flow" />
    ),
    cell: ({ row }) => {
      const value = row.original.maxVolumetricSpeedMm3s;
      return value === undefined ? EM_DASH : `${value} mm³/s`;
    },
  },
  {
    id: 'fanPercent',
    accessorFn: (profile) => profile.fanPercent ?? null,
    header: ({ column }) => (
      <DataTableColumnHeader column={column} title="Fan" />
    ),
    cell: ({ row }) => {
      const value = row.original.fanPercent;
      return value === undefined ? EM_DASH : `${value}%`;
    },
  },
  {
    accessorKey: 'source',
    header: ({ column }) => (
      <DataTableColumnHeader column={column} title="Source" />
    ),
    cell: ({ row }) => (
      <span className="text-sm text-muted-foreground">
        {row.original.source}
      </span>
    ),
  },
];

interface ProfilesTableProps {
  profiles: Profile[];
}

export function ProfilesTable({ profiles }: ProfilesTableProps) {
  return (
    <DataTable
      columns={columns}
      data={profiles}
      emptyState={
        <>No profiles yet. Import a slicer bundle to populate this catalog.</>
      }
    />
  );
}
