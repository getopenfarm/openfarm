import type { ColumnDef } from '@tanstack/react-table';
import { Link } from 'react-router';
import {
  DataTable,
  type ServerTablePagination,
  type ServerTableSorting,
} from '@/components/data-table/DataTable';
import { DataTableColumnHeader } from '@/components/data-table/DataTableColumnHeader';
import type { MachineProfileResponse } from '@/lib/api/generated/endpoints.schemas';

// Every other field (nozzle size, bed size, enclosure, etc.) is one click away on the profile
// detail page — kept out of the catalog list to keep it scannable (research.md's derived-fields design
// still computes them for other consumers, e.g. the filament nozzle-size picker; this table just
// doesn't display them).
const columns: ColumnDef<MachineProfileResponse>[] = [
  {
    accessorKey: 'name',
    header: ({ column }) => (
      <DataTableColumnHeader column={column} title="Name" />
    ),
    cell: ({ row }) => (
      <Link
        to={`/profiles/machine/${row.original.id}`}
        className="font-medium hover:underline"
      >
        {row.original.name}
      </Link>
    ),
  },
  {
    id: 'baseProfileName',
    header: 'Base',
    cell: ({ row }) => row.original.baseProfileName ?? 'Base',
  },
];

interface MachineProfilesTableProps {
  pagination?: ServerTablePagination;
  sorting?: ServerTableSorting;
  profiles: MachineProfileResponse[];
  onRowClick: (id: string) => void;
}

export function MachineProfilesTable({
  profiles,
  pagination,
  sorting,
  onRowClick,
}: MachineProfilesTableProps) {
  return (
    <DataTable
      serverSorting={sorting}
      serverPagination={pagination}
      columns={columns}
      data={profiles}
      onRowClick={(row) => row.id && onRowClick(row.id)}
    />
  );
}
