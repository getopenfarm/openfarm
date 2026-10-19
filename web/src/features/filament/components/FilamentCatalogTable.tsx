import type { ColumnDef } from '@tanstack/react-table';
import { PlusIcon } from 'lucide-react';
import { Link, useNavigate } from 'react-router';
import { DataTable } from '@/components/data-table/DataTable';
import { DataTableColumnHeader } from '@/components/data-table/DataTableColumnHeader';
import { DataTableRowActions } from '@/components/data-table/DataTableRowActions';
import { StatusBadge } from '@/components/status-badge/StatusBadge';
import { Swatch } from '@/components/swatch/Swatch';
import {
  DropdownMenuGroup,
  DropdownMenuItem,
} from '@/components/ui/dropdown-menu';
import type { FilamentResponse } from '@/lib/api/generated/endpoints.schemas';

import { formatCost } from '@/lib/formatUnits';

const MAX_VISIBLE_SWATCHES = 4;

interface FilamentCatalogTableProps {
  filaments: FilamentResponse[];
}

export function FilamentCatalogTable({ filaments }: FilamentCatalogTableProps) {
  const navigate = useNavigate();

  const columns: ColumnDef<FilamentResponse>[] = [
    {
      id: 'filament',
      accessorFn: (row) => `${row.vendorName} · ${row.material}`,
      meta: { label: 'Filament' },
      header: ({ column }) => (
        <DataTableColumnHeader column={column} title="Filament" />
      ),
      cell: ({ row }) => (
        <Link
          to={`/filament/${row.original.id}/edit`}
          className="font-medium hover:underline"
        >
          {row.original.vendorName} · {row.original.material}
        </Link>
      ),
    },
    {
      id: 'type',
      accessorFn: (row) => row.materialType,
      meta: { label: 'Type' },
      header: ({ column }) => (
        <DataTableColumnHeader column={column} title="Type" />
      ),
      cell: ({ row }) => (
        <StatusBadge kind="zinc" label={row.original.materialType ?? ''} />
      ),
    },
    {
      id: 'pricePerKg',
      header: 'Price per kg',
      accessorKey: 'pricePerKg',
      cell: ({ row }) =>
        row.original.pricePerKg == null
          ? '—'
          : formatCost(row.original.pricePerKg),
    },
    {
      id: 'colors',
      header: 'Colors',
      cell: ({ row }) => {
        const variants = row.original.variants ?? [];
        const visible = variants.slice(0, MAX_VISIBLE_SWATCHES);
        const overflow = variants.length - visible.length;
        return (
          <div className="flex items-center gap-1">
            {visible.map((variant) => (
              <Swatch key={variant.id} colorHex={variant.colorHex} size="sm" />
            ))}
            {overflow > 0 && (
              <span className="text-xs text-muted-foreground">+{overflow}</span>
            )}
          </div>
        );
      },
    },
    {
      id: 'actions',
      enableSorting: false,
      header: () => <span className="sr-only">Actions</span>,
      cell: ({ row }) => (
        <DataTableRowActions
          label={`${row.original.vendorName} ${row.original.material}`}
          disabled={!row.original.id}
        >
          <DropdownMenuGroup>
            <DropdownMenuItem
              onSelect={() => navigate(`/filament/${row.original.id}/edit`)}
            >
              <PlusIcon /> Add variant
            </DropdownMenuItem>
          </DropdownMenuGroup>
        </DataTableRowActions>
      ),
    },
  ];

  return (
    <DataTable
      pageSize={25}
      columns={columns}
      data={filaments}
      toolbar={null}
      onRowClick={(row) => navigate(`/filament/${row.id}/edit`)}
    />
  );
}
