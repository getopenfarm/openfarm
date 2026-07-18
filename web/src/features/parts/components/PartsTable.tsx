import type { ColumnDef } from '@tanstack/react-table';
import { PrinterIcon } from 'lucide-react';
import { CheckerboardThumbnail } from '@/components/checkerboard-thumbnail/CheckerboardThumbnail';
import { DataTable } from '@/components/data-table/DataTable';
import { DataTableColumnHeader } from '@/components/data-table/DataTableColumnHeader';
import { Swatch } from '@/components/swatch/Swatch';
import { Button } from '@/components/ui/button';
import type { FilamentResponse } from '@/lib/api/generated/endpoints.schemas';
import { formatMinutes } from '@/lib/formatMinutes';
import type { Part } from '../data/mockPartsStore';
import { resolveSlotDisplayColor } from '../lib/colorMatch';

interface PartsTableProps {
  parts: Part[];
  filaments: FilamentResponse[];
  onOpen: (id: string) => void;
  onPrint: (part: Part) => void;
}

export function PartsTable({
  parts,
  filaments,
  onOpen,
  onPrint,
}: PartsTableProps) {
  const columns: ColumnDef<Part>[] = [
    {
      id: 'part',
      accessorKey: 'name',
      header: ({ column }) => (
        <DataTableColumnHeader column={column} title="Part" />
      ),
      cell: ({ row }) => (
        <div className="flex items-center gap-3">
          <CheckerboardThumbnail
            label={row.original.fileFormat}
            className="size-10 rounded-md"
          />
          <div className="flex flex-col">
            <span className="font-medium">{row.original.name}</span>
            <span className="font-mono text-xs text-muted-foreground">
              v{row.original.revision}
            </span>
          </div>
        </div>
      ),
    },
    {
      id: 'material',
      header: 'Material',
      cell: ({ row }) => {
        const slot = row.original.colorSlots[0];
        if (!slot) {
          return <span className="text-muted-foreground">—</span>;
        }
        return (
          <div className="flex items-center gap-2">
            <Swatch
              colorHex={resolveSlotDisplayColor(slot, filaments)}
              size="sm"
            />
            <span>{slot.materialType}</span>
          </div>
        );
      },
    },
    {
      id: 'time',
      accessorKey: 'estimatedTimeMinutes',
      header: ({ column }) => (
        <DataTableColumnHeader column={column} title="Time" />
      ),
      cell: ({ row }) => (
        <span className="font-mono text-sm">
          {formatMinutes(row.original.estimatedTimeMinutes)}
        </span>
      ),
    },
    {
      id: 'weight',
      accessorKey: 'weightGrams',
      header: ({ column }) => (
        <DataTableColumnHeader column={column} title="Weight" />
      ),
      cell: ({ row }) => (
        <span className="font-mono text-sm">{row.original.weightGrams} g</span>
      ),
    },
    {
      id: 'files',
      header: 'Files',
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">
          {row.original.fileFormat}
        </span>
      ),
    },
    {
      id: 'actions',
      header: () => <span className="sr-only">Actions</span>,
      cell: ({ row }) => (
        <div className="flex justify-end">
          <Button
            variant="outline"
            size="sm"
            onClick={(event) => {
              event.stopPropagation();
              onPrint(row.original);
            }}
          >
            <PrinterIcon /> Print
          </Button>
        </div>
      ),
    },
  ];

  return (
    <DataTable
      columns={columns}
      data={parts}
      onRowClick={(row) => onOpen(row.id)}
      emptyState="No parts match."
    />
  );
}
