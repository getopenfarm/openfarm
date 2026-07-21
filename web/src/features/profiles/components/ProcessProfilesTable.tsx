import type { ColumnDef } from '@tanstack/react-table';
import { DataTable } from '@/components/data-table/DataTable';
import type { ProcessProfileResponse } from '@/lib/api/generated/endpoints.schemas';

// Mirrors MachineProfilesTable/FilamentProfilesTable — every other field (compatible printers,
// wall loops, etc.) is one click away in the raw data dialog, kept out of the catalog list to keep
// it scannable.
const columns: ColumnDef<ProcessProfileResponse>[] = [
  { accessorKey: 'name', header: 'Name' },
  {
    id: 'layerHeightMm',
    header: 'Layer height',
    cell: ({ row }) =>
      row.original.layerHeightMm !== undefined &&
      row.original.layerHeightMm !== null
        ? `${row.original.layerHeightMm}mm`
        : '—',
  },
  {
    id: 'baseProfileName',
    header: 'Base',
    cell: ({ row }) => row.original.baseProfileName ?? 'Base',
  },
];

interface ProcessProfilesTableProps {
  profiles: ProcessProfileResponse[];
  onRowClick: (id: string) => void;
}

export function ProcessProfilesTable({
  profiles,
  onRowClick,
}: ProcessProfilesTableProps) {
  return (
    <DataTable
      columns={columns}
      data={profiles}
      onRowClick={(row) => row.id && onRowClick(row.id)}
    />
  );
}
