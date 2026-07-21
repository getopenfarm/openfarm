import type { ColumnDef } from '@tanstack/react-table';
import { DataTable } from '@/components/data-table/DataTable';
import type { MachineProfileResponse } from '@/lib/api/generated/endpoints.schemas';

// Every other field (nozzle size, bed size, enclosure, etc.) is one click away in the raw data
// dialog — kept out of the catalog list to keep it scannable (research.md's derived-fields design
// still computes them for other consumers, e.g. the filament nozzle-size picker; this table just
// doesn't display them).
const columns: ColumnDef<MachineProfileResponse>[] = [
  { accessorKey: 'name', header: 'Name' },
  {
    id: 'baseProfileName',
    header: 'Base',
    cell: ({ row }) => row.original.baseProfileName ?? 'Base',
  },
];

interface MachineProfilesTableProps {
  profiles: MachineProfileResponse[];
  onRowClick: (id: string) => void;
}

export function MachineProfilesTable({
  profiles,
  onRowClick,
}: MachineProfilesTableProps) {
  return (
    <DataTable
      columns={columns}
      data={profiles}
      onRowClick={(row) => row.id && onRowClick(row.id)}
    />
  );
}
