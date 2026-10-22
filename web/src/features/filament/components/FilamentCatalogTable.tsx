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
import type {
  FilamentResponse,
  FilamentShortageResponse,
  VariantStockResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { formatCost, formatWeight } from '@/lib/formatUnits';
import { type StockTotals, sumStock } from '../lib/stock';

const MAX_VISIBLE_SWATCHES = 4;

interface FilamentCatalogTableProps {
  filaments: FilamentResponse[];
  /** Per-colour stock from the same list response. */
  stock: VariantStockResponse[];
  /** Colours queued finite work needs more of than is on hand. */
  shortages?: FilamentShortageResponse[];
}

export function FilamentCatalogTable({
  filaments,
  stock,
  shortages = [],
}: FilamentCatalogTableProps) {
  const navigate = useNavigate();
  const byVariant = new Map(stock.map((row) => [row.filamentVariantId, row]));
  const shortOf = (filament: FilamentResponse) => {
    const ids = new Set((filament.variants ?? []).map((variant) => variant.id));
    return shortages
      .filter((short) => ids.has(short.filamentVariantId))
      .reduce(
        (sum, short) => ({
          queued: sum.queued + (short.queuedGrams ?? 0),
          onHand: sum.onHand + (short.onHandGrams ?? 0),
        }),
        { queued: 0, onHand: 0 },
      );
  };
  const totals = (filament: FilamentResponse): StockTotals =>
    sumStock(
      (filament.variants ?? []).flatMap((variant) => {
        const row = byVariant.get(variant.id);
        return row ? [row] : [];
      }),
    );

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
      id: 'onHand',
      accessorFn: (row) => totals(row).onHandGrams,
      meta: { label: 'On hand' },
      header: ({ column }) => (
        <DataTableColumnHeader column={column} title="On hand" />
      ),
      cell: ({ row }) => {
        const { onHandGrams, lowColors } = totals(row.original);
        const short = shortOf(row.original);
        return (
          <span className="flex items-center gap-2 whitespace-nowrap">
            <span className="tabular-nums">{formatWeight(onHandGrams)}</span>
            {lowColors > 0 && (
              <StatusBadge kind="amber" label={`${lowColors} low`} />
            )}
            {short.queued > 0 && (
              <span
                title={`Queued ${formatWeight(short.queued)} · on hand ${formatWeight(short.onHand)}`}
              >
                <StatusBadge
                  kind="amber"
                  label={`Short ${formatWeight(short.queued - short.onHand)}`}
                />
              </span>
            )}
          </span>
        );
      },
    },
    {
      id: 'value',
      accessorFn: (row) => totals(row).value,
      meta: { label: 'Value' },
      header: ({ column }) => (
        <DataTableColumnHeader column={column} title="Value" />
      ),
      cell: ({ row }) => {
        const { activeSpools, value, unpricedSpools } = totals(row.original);
        if (activeSpools === 0) return '—';
        return (
          <span className="flex items-center gap-2 whitespace-nowrap">
            <span className="tabular-nums">{formatCost(value)}</span>
            {unpricedSpools > 0 && <StatusBadge kind="amber" label="Partial" />}
          </span>
        );
      },
    },
    {
      id: 'used',
      accessorFn: (row) => totals(row).usedGrams,
      meta: { label: 'Used 30 d' },
      header: ({ column }) => (
        <DataTableColumnHeader column={column} title="Used 30 d" />
      ),
      cell: ({ row }) => (
        <span className="tabular-nums">
          {formatWeight(totals(row.original).usedGrams)}
        </span>
      ),
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
