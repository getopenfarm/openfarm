import { ChartColumnIcon } from 'lucide-react';
import { serverListQueryOptions } from '@/components/data-table/useServerListState';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { Swatch } from '@/components/swatch/Swatch';
import { Skeleton } from '@/components/ui/skeleton';
import {
  Table,
  TableBody,
  TableCell,
  TableFooter,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import type { MaterialsReportRowResponse } from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsReportsGetMaterialsReportEndpoint } from '@/lib/api/generated/reports/reports';
import { formatCost, formatPercent, formatWeight } from '@/lib/formatUnits';

function Figures({ row }: { row: MaterialsReportRowResponse }) {
  const unpriced = row.unpricedGrams ?? 0;
  return (
    <>
      <TableCell className="text-right tabular-nums">
        {formatWeight(row.usedGrams ?? 0)}
      </TableCell>
      <TableCell className="text-right tabular-nums">
        {formatWeight(row.wasteGrams ?? 0)}
      </TableCell>
      <TableCell className="text-right tabular-nums">
        {row.wasteShare == null ? '—' : formatPercent(row.wasteShare)}
      </TableCell>
      <TableCell className="text-right tabular-nums">
        {formatWeight(row.writtenOffGrams ?? 0)}
      </TableCell>
      <TableCell className="text-right tabular-nums">
        {formatCost(row.cost ?? 0)}
        {unpriced > 0 && (
          <span className="block text-xs text-muted-foreground">
            Partial · {formatWeight(unpriced)} unpriced
          </span>
        )}
      </TableCell>
      <TableCell className="text-right tabular-nums">
        {row.printCount ?? 0}
      </TableCell>
      <TableCell className="text-right tabular-nums">
        {row.notDeductedCount || '—'}
      </TableCell>
    </>
  );
}

/**
 * Filament used, wasted and written off per color in the range, then the lines no spool was deducted from, and a total.
 * With `filamentId`, only that filament's colors: rows name just the color, and there is no no-spool row.
 */
export function MaterialsReportTable({
  range,
  filamentId,
}: {
  range: { from: string; to: string };
  filamentId?: string;
}) {
  const query = useOpenFarmApiEndpointsReportsGetMaterialsReportEndpoint(
    filamentId ? { ...range, filamentId } : range,
    { query: serverListQueryOptions },
  );
  const report = query.data?.status === 200 ? query.data.data : undefined;
  const rows = report?.rows ?? [];

  if (query.isLoading) return <Skeleton className="h-40 w-full" />;
  if (!report) {
    return (
      <ErrorState
        description="Could not load the report."
        onRetry={() => query.refetch()}
      />
    );
  }
  if (rows.length === 0) {
    return (
      <EmptyState
        icon={<ChartColumnIcon className="size-8 text-muted-foreground" />}
        title="No filament used in this range."
      />
    );
  }
  return (
    <div
      className="flex flex-col gap-2"
      aria-busy={query.isFetching || undefined}
    >
      <div className="overflow-hidden rounded-md border">
        <Table
          aria-label={filamentId ? 'Filament by color' : 'Filament by material'}
        >
          <TableHeader>
            <TableRow>
              <TableHead>{filamentId ? 'Color' : 'Material'}</TableHead>
              <TableHead className="text-right">Used</TableHead>
              <TableHead className="text-right">Waste</TableHead>
              <TableHead className="text-right">Waste %</TableHead>
              <TableHead className="text-right">Written off</TableHead>
              <TableHead className="text-right">Cost</TableHead>
              <TableHead className="text-right">Prints</TableHead>
              <TableHead className="text-right">Not deducted</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {rows.map((row) => (
              <TableRow key={row.filamentVariantId ?? 'no-spool'}>
                <TableCell>
                  {row.filamentVariantId ? (
                    <span className="flex items-center gap-2">
                      <Swatch colorHex={row.colorHex ?? undefined} size="sm" />
                      {filamentId
                        ? row.colorName
                        : `${row.vendorName} ${row.material} · ${row.colorName}`}
                    </span>
                  ) : (
                    <span className="text-muted-foreground">No spool</span>
                  )}
                </TableCell>
                <Figures row={row} />
              </TableRow>
            ))}
          </TableBody>
          {report.total && (
            <TableFooter>
              <TableRow>
                <TableCell>Total</TableCell>
                <Figures row={report.total} />
              </TableRow>
            </TableFooter>
          )}
        </Table>
      </div>
      <p className="text-xs text-muted-foreground">
        Used: completed prints. Waste: failed and cancelled prints. Written off:
        finished spools, net of restores.
      </p>
    </div>
  );
}
