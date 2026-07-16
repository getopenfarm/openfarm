import type { ColumnDef } from '@tanstack/react-table';
import { DataTable } from '@/components/data-table/DataTable';
import type { MachineProfileResponse } from '@/lib/api/generated/endpoints.schemas';

const EM_DASH = '—';

const columns: ColumnDef<MachineProfileResponse>[] = [
  { accessorKey: 'name', header: 'Name' },
  { accessorKey: 'sourceFormat', header: 'Source' },
  {
    id: 'nozzleSizeMm',
    accessorFn: (p) => p.nozzleSizeMm ?? null,
    header: 'Nozzle size',
    cell: ({ row }) =>
      row.original.nozzleSizeMm != null
        ? `${row.original.nozzleSizeMm}mm`
        : EM_DASH,
  },
  {
    id: 'bedSize',
    header: 'Bed size',
    cell: ({ row }) =>
      row.original.bedSizeXMm != null && row.original.bedSizeYMm != null
        ? `${row.original.bedSizeXMm}×${row.original.bedSizeYMm}mm`
        : EM_DASH,
  },
  {
    id: 'maxHeightMm',
    accessorFn: (p) => p.maxHeightMm ?? null,
    header: 'Max height',
    cell: ({ row }) =>
      row.original.maxHeightMm != null
        ? `${row.original.maxHeightMm}mm`
        : EM_DASH,
  },
  {
    id: 'hasEnclosure',
    header: 'Enclosure',
    cell: ({ row }) =>
      row.original.hasEnclosure == null
        ? EM_DASH
        : row.original.hasEnclosure
          ? 'Yes'
          : 'No',
  },
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
      emptyState={
        <>
          No machine profiles yet. Import a slicer bundle to populate this
          catalog.
        </>
      }
    />
  );
}
