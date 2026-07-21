import type { ColumnDef } from '@tanstack/react-table';
import { DataTable } from '@/components/data-table/DataTable';
import type { FilamentProfileResponse } from '@/lib/api/generated/endpoints.schemas';

// Every other field (material, brand, color, temps, compatible printers) is one click away in
// the raw data dialog — kept out of the catalog list to keep it scannable (research.md's
// derived-fields design still computes them for other consumers, e.g. the filament nozzle-size
// picker; this table just doesn't display them).
const columns: ColumnDef<FilamentProfileResponse>[] = [
  { accessorKey: 'name', header: 'Name' },
  {
    id: 'baseProfileName',
    header: 'Base',
    cell: ({ row }) => row.original.baseProfileName ?? 'Base',
  },
];

interface FilamentProfilesTableProps {
  profiles: FilamentProfileResponse[];
  onRowClick: (id: string) => void;
}

export function FilamentProfilesTable({
  profiles,
  onRowClick,
}: FilamentProfilesTableProps) {
  return (
    <DataTable
      columns={columns}
      data={profiles}
      onRowClick={(row) => row.id && onRowClick(row.id)}
    />
  );
}
