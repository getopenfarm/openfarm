import type { ColumnDef } from '@tanstack/react-table';
import { ImageOffIcon, LoaderIcon } from 'lucide-react';
import { DataTable } from '@/components/data-table/DataTable';
import { DataTableColumnHeader } from '@/components/data-table/DataTableColumnHeader';
import { Badge } from '@/components/ui/badge';
import type { PartSummaryResponse } from '@/lib/api/generated/endpoints.schemas';

interface PartsTableProps {
  parts: PartSummaryResponse[];
  onRowClick: (part: PartSummaryResponse) => void;
}

function EstimateCell({ part }: { part: PartSummaryResponse }) {
  if (part.estimateStatus === 'Available') {
    return (
      <span className="font-mono text-sm">
        {part.estimatedPrintTimeMinutes} min ·{' '}
        {part.estimatedFilamentWeightGrams} g
      </span>
    );
  }
  if (part.estimateStatus === 'Unavailable') {
    return (
      <span className="text-sm text-muted-foreground">
        Estimate unavailable
      </span>
    );
  }
  return (
    <span className="flex items-center gap-1.5 text-sm text-muted-foreground">
      <LoaderIcon className="size-3.5 animate-spin" />
      Estimating…
    </span>
  );
}

const columns: ColumnDef<PartSummaryResponse>[] = [
  {
    id: 'part',
    accessorKey: 'name',
    header: ({ column }) => (
      <DataTableColumnHeader column={column} title="Part" />
    ),
    cell: ({ row }) => (
      <div className="flex items-center gap-3">
        <div className="flex size-10 shrink-0 items-center justify-center overflow-hidden rounded-md bg-muted text-muted-foreground">
          {row.original.thumbnailBase64 ? (
            <img
              src={`data:image/png;base64,${row.original.thumbnailBase64}`}
              alt=""
              className="size-full object-contain"
            />
          ) : (
            <ImageOffIcon className="size-4" />
          )}
        </div>
        <div className="flex flex-col">
          <span className="font-medium">{row.original.name}</span>
          <span className="text-xs text-muted-foreground">
            {row.original.fileFormat}
          </span>
        </div>
      </div>
    ),
  },
  {
    id: 'material',
    header: 'Material',
    cell: ({ row }) => {
      const materialTypes = row.original.materialTypes ?? [];
      if (materialTypes.length === 0) {
        return (
          <Badge variant="secondary" className="font-normal">
            Any material
          </Badge>
        );
      }
      return (
        <div className="flex flex-wrap gap-1">
          {materialTypes.map((material) => (
            <Badge key={material} variant="secondary" className="font-normal">
              {material}
            </Badge>
          ))}
        </div>
      );
    },
  },
  {
    id: 'nozzleDiameter',
    header: 'Nozzle',
    cell: ({ row }) => (
      <span className="font-mono text-sm">
        {row.original.nozzleDiameterMm} mm
      </span>
    ),
  },
  {
    id: 'estimate',
    header: 'Estimate',
    cell: ({ row }) => <EstimateCell part={row.original} />,
  },
  {
    id: 'tags',
    header: 'Tags',
    cell: ({ row }) => (
      <div className="flex flex-wrap gap-1">
        {(row.original.tags ?? []).map((tag) => (
          <Badge key={tag} variant="outline" className="font-normal">
            {tag}
          </Badge>
        ))}
      </div>
    ),
  },
];

export function PartsTable({ parts, onRowClick }: PartsTableProps) {
  return (
    <DataTable
      columns={columns}
      data={parts}
      onRowClick={onRowClick}
      emptyState={
        <div className="flex flex-col items-center gap-1 py-4">
          <p className="text-sm font-medium">No parts yet</p>
          <p className="text-sm text-muted-foreground">
            Add a part to build your catalog.
          </p>
        </div>
      }
    />
  );
}
