import type { ColumnDef } from '@tanstack/react-table';
import { PlusIcon } from 'lucide-react';
import { useNavigate } from 'react-router';
import { DataTable } from '@/components/data-table/DataTable';
import { DataTableColumnHeader } from '@/components/data-table/DataTableColumnHeader';
import { StatusBadge } from '@/components/status-badge/StatusBadge';
import { Swatch } from '@/components/swatch/Swatch';
import { Button } from '@/components/ui/button';
import type { FilamentResponse } from '@/lib/api/generated/endpoints.schemas';

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
      header: ({ column }) => (
        <DataTableColumnHeader column={column} title="Filament" />
      ),
      cell: ({ row }) => (
        <span className="font-medium">
          {row.original.vendorName} · {row.original.material}
        </span>
      ),
    },
    {
      id: 'type',
      accessorFn: (row) => row.materialType,
      header: ({ column }) => (
        <DataTableColumnHeader column={column} title="Type" />
      ),
      cell: ({ row }) => (
        <StatusBadge kind="zinc" label={row.original.materialType ?? ''} />
      ),
    },
    {
      id: 'colours',
      header: 'Colours',
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
      id: 'variantCount',
      header: 'Variants',
      cell: ({ row }) => (
        <span className="text-sm">{row.original.variants?.length ?? 0}</span>
      ),
    },
    {
      id: 'addVariant',
      header: () => <span className="sr-only">Add variant</span>,
      cell: ({ row }) => (
        <div className="flex justify-end">
          <Button
            variant="ghost"
            size="icon-sm"
            aria-label={`Add variant to ${row.original.vendorName} ${row.original.material}`}
            onClick={(event) => {
              event.stopPropagation();
              navigate(`/filament/${row.original.id}/edit`);
            }}
          >
            <PlusIcon />
          </Button>
        </div>
      ),
    },
  ];

  return (
    <DataTable
      columns={columns}
      data={filaments}
      onRowClick={(row) => navigate(`/filament/${row.id}/edit`)}
      emptyState="No filaments in the catalog yet."
    />
  );
}
