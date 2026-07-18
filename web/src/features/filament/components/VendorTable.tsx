import type { ColumnDef } from '@tanstack/react-table';
import { DataTable } from '@/components/data-table/DataTable';
import { DataTableColumnHeader } from '@/components/data-table/DataTableColumnHeader';
import type {
  ListVendorsResponse,
  VendorResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsVendorsListVendorsEndpoint } from '@/lib/api/generated/vendors/vendors';

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

export function VendorTable() {
  const vendorsQuery = useOpenFarmApiEndpointsVendorsListVendorsEndpoint();
  const vendors =
    (vendorsQuery.data?.data as ListVendorsResponse | undefined)?.vendors ?? [];

  return (
    <DataTable columns={columns} data={vendors} emptyState="No vendors yet." />
  );
}
