import type { ColumnDef } from '@tanstack/react-table';
import { DataTable } from '@/components/data-table/DataTable';
import { Swatch } from '@/components/swatch/Swatch';
import type { FilamentProfileResponse } from '@/lib/api/generated/endpoints.schemas';

const EM_DASH = '—';

const columns: ColumnDef<FilamentProfileResponse>[] = [
  { accessorKey: 'name', header: 'Name' },
  {
    id: 'material',
    header: 'Material',
    cell: ({ row }) => (
      <div className="flex items-center gap-2">
        {row.original.colorHex && <Swatch colorHex={row.original.colorHex} />}
        <span>{row.original.material ?? EM_DASH}</span>
      </div>
    ),
  },
  {
    accessorKey: 'brand',
    header: 'Brand',
    cell: ({ row }) => row.original.brand ?? EM_DASH,
  },
  {
    id: 'nozzleTempC',
    accessorFn: (p) => p.nozzleTempC ?? null,
    header: 'Nozzle temp',
    cell: ({ row }) =>
      row.original.nozzleTempC != null
        ? `${row.original.nozzleTempC}°C`
        : EM_DASH,
  },
  {
    id: 'bedTempC',
    accessorFn: (p) => p.bedTempC ?? null,
    header: 'Bed temp',
    cell: ({ row }) =>
      row.original.bedTempC != null ? `${row.original.bedTempC}°C` : EM_DASH,
  },
  {
    id: 'compatiblePrinterNames',
    header: 'Compatible printers',
    cell: ({ row }) => {
      const names = row.original.compatiblePrinterNames ?? [];
      return names.length > 0 ? names.join(', ') : 'All printers';
    },
  },
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
      emptyState={
        <>
          No filament profiles yet. Import a slicer bundle to populate this
          catalog.
        </>
      }
    />
  );
}
