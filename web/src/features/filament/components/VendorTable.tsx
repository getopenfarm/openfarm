import type { ColumnDef } from '@tanstack/react-table';
import { DataTable } from '@/components/data-table/DataTable';
import { DataTableColumnHeader } from '@/components/data-table/DataTableColumnHeader';
import type { VendorResponse } from '@/lib/api/generated/endpoints.schemas';

const columns: ColumnDef<VendorResponse>[] = [
  {
    id: 'name',
    accessorKey: 'name',
    header: ({ column }) => (
      <DataTableColumnHeader column={column} title="Vendor" />
    ),
    cell: ({ row }) => <span className="font-medium">{row.original.name}</span>,
  },
];

interface VendorTableProps {
  vendors: VendorResponse[];
}

export function VendorTable({ vendors }: VendorTableProps) {
  return <DataTable pageSize={25} columns={columns} data={vendors} />;
}
