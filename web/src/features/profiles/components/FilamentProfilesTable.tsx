import type { ColumnDef } from '@tanstack/react-table';
import { Link } from 'react-router';
import {
  DataTable,
  type ServerTablePagination,
  type ServerTableSorting,
} from '@/components/data-table/DataTable';
import { DataTableColumnHeader } from '@/components/data-table/DataTableColumnHeader';
import type { FilamentProfileResponse } from '@/lib/api/generated/endpoints.schemas';

// Every other field (material, brand, color, temps, compatible printers) is one click away on
// the profile detail page — kept out of the catalog list to keep it scannable (research.md's
// derived-fields design still computes them for other consumers, e.g. the filament nozzle-size
// picker; this table just doesn't display them).
const columns: ColumnDef<FilamentProfileResponse>[] = [
  {
    accessorKey: 'name',
    header: ({ column }) => (
      <DataTableColumnHeader column={column} title="Name" />
    ),
    cell: ({ row }) => (
      <Link
        to={`/profiles/filament/${row.original.id}`}
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

interface FilamentProfilesTableProps {
  pagination?: ServerTablePagination;
  sorting?: ServerTableSorting;
  profiles: FilamentProfileResponse[];
  onRowClick: (id: string) => void;
}

export function FilamentProfilesTable({
  profiles,
  pagination,
  sorting,
  onRowClick,
}: FilamentProfilesTableProps) {
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
