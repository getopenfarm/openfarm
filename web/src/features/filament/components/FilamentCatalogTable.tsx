import type { ColumnDef } from '@tanstack/react-table';
import { PencilIcon } from 'lucide-react';
import { useNavigate } from 'react-router';
import { DataTable } from '@/components/data-table/DataTable';
import { DataTableColumnHeader } from '@/components/data-table/DataTableColumnHeader';
import { StatusBadge } from '@/components/status-badge/StatusBadge';
import { Swatch } from '@/components/swatch/Swatch';
import { Button } from '@/components/ui/button';
import type { Filament } from '../data/mockFilamentStore';
import { formatWeightGrams } from '../lib/weightLabel';

const MAX_VISIBLE_SWATCHES = 4;

function uniqueWeightsLabel(filament: Filament): string {
  const weights = new Set<number>();
  for (const variant of filament.variants) {
    for (const grams of variant.weightsGrams) {
      weights.add(grams);
    }
  }
  return Array.from(weights)
    .sort((a, b) => a - b)
    .map(formatWeightGrams)
    .join(' · ');
}

interface FilamentCatalogTableProps {
  filaments: Filament[];
}

export function FilamentCatalogTable({ filaments }: FilamentCatalogTableProps) {
  const navigate = useNavigate();

  const columns: ColumnDef<Filament>[] = [
    {
      id: 'filament',
      accessorFn: (row) => `${row.brand} · ${row.material}`,
      header: ({ column }) => (
        <DataTableColumnHeader column={column} title="Filament" />
      ),
      cell: ({ row }) => (
        <div className="flex flex-col gap-0.5">
          <span className="font-medium">
            {row.original.brand} · {row.original.material}
          </span>
          <span className="font-mono text-xs text-muted-foreground">
            {row.original.diameterMm}mm
          </span>
        </div>
      ),
    },
    {
      id: 'type',
      accessorFn: (row) => row.materialType,
      header: ({ column }) => (
        <DataTableColumnHeader column={column} title="Type" />
      ),
      cell: ({ row }) => (
        <StatusBadge kind="zinc" label={row.original.materialType} />
      ),
    },
    {
      id: 'colours',
      header: 'Colours',
      cell: ({ row }) => {
        const variants = row.original.variants;
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
      id: 'weights',
      header: 'Weights',
      cell: ({ row }) => (
        <span className="font-mono text-xs text-muted-foreground">
          {uniqueWeightsLabel(row.original)}
        </span>
      ),
    },
    {
      id: 'inStock',
      header: 'In stock',
      cell: ({ row }) => (
        <span className="text-sm">{row.original.variants.length}</span>
      ),
    },
    {
      id: 'edit',
      header: () => <span className="sr-only">Edit</span>,
      cell: ({ row }) => (
        <div className="flex justify-end">
          <Button
            variant="ghost"
            size="icon-sm"
            aria-label={`Edit ${row.original.brand} ${row.original.material}`}
            onClick={(event) => {
              event.stopPropagation();
              navigate(`/filament/${row.original.id}/edit`);
            }}
          >
            <PencilIcon />
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
