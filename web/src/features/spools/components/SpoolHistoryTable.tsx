import { HistoryIcon } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router';
import { TablePagination } from '@/components/data-table/TablePagination';
import {
  serverListQueryOptions,
  useRetainedListData,
  useServerListState,
  useServerPageRecovery,
} from '@/components/data-table/useServerListState';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { Swatch } from '@/components/swatch/Swatch';
import { Separator } from '@/components/ui/separator';
import { Skeleton } from '@/components/ui/skeleton';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import type {
  OpenFarmApiEndpointsSpoolsExportSpoolHistoryEndpointParams,
  SpoolHistoryEntryResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsSpoolsListSpoolHistoryEndpoint } from '@/lib/api/generated/spools/spools';
import { formatDateTime } from '@/lib/formatDateTime';
import { formatGrams } from '@/lib/formatUnits';
import { formatChange, SourceCell } from './SpoolHistoryCard';

/** The spool, named by its label or else its color; a labeled spool shows its color under it. The material is the page's. */
function SpoolCell({ entry }: { entry: SpoolHistoryEntryResponse }) {
  return (
    <div className="flex flex-col">
      <Link
        to={`/filament/spools/${entry.spoolId}`}
        className="flex items-center gap-2 font-medium underline-offset-4 hover:underline"
      >
        <Swatch colorHex={entry.colorHex ?? undefined} size="sm" />
        {entry.spoolLabel || entry.colorName || 'Spool'}
      </Link>
      {entry.spoolLabel && (
        <span className="text-xs text-muted-foreground">{entry.colorName}</span>
      )}
    </div>
  );
}

/** One filament's spool weight changes in the filter, newest first, a page at a time. A new filter starts again at page 1. */
export function SpoolHistoryTable({
  filter,
}: {
  filter: OpenFarmApiEndpointsSpoolsExportSpoolHistoryEndpointParams;
}) {
  const { page, pageSize, setPage, setPageSize } = useServerListState(
    'recordedAtUtc',
    'recordedAtUtc',
  );
  const filterKey = JSON.stringify(filter);
  const [shownFilterKey, setShownFilterKey] = useState(filterKey);
  if (shownFilterKey !== filterKey) {
    setShownFilterKey(filterKey);
    setPage(1);
  }

  const query = useOpenFarmApiEndpointsSpoolsListSpoolHistoryEndpoint(
    { ...filter, page, pageSize },
    { query: serverListQueryOptions },
  );
  const data = useRetainedListData(
    query.data?.status === 200 ? query.data.data : undefined,
  );
  const entries = data?.entries ?? [];
  const totalCount = data?.totalCount ?? 0;
  useServerPageRecovery(
    page,
    pageSize,
    totalCount,
    query.isSuccess && !query.isFetching && !query.isPlaceholderData,
    setPage,
  );

  return (
    <div
      aria-busy={query.isFetching || undefined}
      className="overflow-hidden rounded-md border"
    >
      {query.isLoading ? (
        <Skeleton className="m-4 h-24" />
      ) : query.isError && !data ? (
        <ErrorState
          description="Could not load history."
          onRetry={() => query.refetch()}
          bordered={false}
        />
      ) : entries.length === 0 ? (
        <EmptyState
          icon={<HistoryIcon className="size-8 text-muted-foreground" />}
          title="No changes in this range."
          bordered={false}
        />
      ) : (
        <Table aria-label="Spool history">
          <TableHeader>
            <TableRow>
              <TableHead>When</TableHead>
              <TableHead>Spool</TableHead>
              <TableHead className="text-right">Change</TableHead>
              <TableHead className="text-right">Remaining</TableHead>
              <TableHead>Source</TableHead>
              <TableHead>Reason</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {entries.map((entry) => (
              <TableRow key={entry.id}>
                <TableCell className="whitespace-nowrap">
                  {entry.recordedAtUtc
                    ? formatDateTime(entry.recordedAtUtc)
                    : '—'}
                </TableCell>
                <TableCell>
                  <SpoolCell entry={entry} />
                </TableCell>
                <TableCell className="text-right tabular-nums">
                  {formatChange(entry.changeGrams ?? 0)}
                </TableCell>
                <TableCell className="text-right tabular-nums">
                  {entry.remainingAfterGrams != null
                    ? formatGrams(entry.remainingAfterGrams)
                    : '—'}
                </TableCell>
                <TableCell>
                  <SourceCell entry={entry} />
                </TableCell>
                <TableCell className="text-muted-foreground">
                  {entry.reason || '—'}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      <Separator />
      <TablePagination
        label="Spool history"
        page={page}
        pageSize={pageSize}
        totalCount={totalCount}
        disabled={query.isFetching || query.isError || query.isPlaceholderData}
        unavailable={query.isError && !data}
        onPageChange={setPage}
        onPageSizeChange={setPageSize}
      />
    </div>
  );
}
