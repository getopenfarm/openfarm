import { Link } from 'react-router';
import { ErrorState } from '@/components/empty-state/EmptyState';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
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
  SpoolHistoryEntryResponse,
  SpoolHistoryResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsSpoolsGetSpoolHistoryEndpoint } from '@/lib/api/generated/spools/spools';
import { formatDateTime } from '@/lib/formatDateTime';
import { formatGrams } from '@/lib/formatUnits';

const HISTORY_PAGE_SIZE = 50;

/** -12.4 g when filament was used or written off, +30 g when a correction or a restore put some back. */
function formatChange(grams: number) {
  return `${grams < 0 ? '−' : '+'}${formatGrams(Math.abs(grams))}`;
}

function SourceCell({ entry }: { entry: SpoolHistoryEntryResponse }) {
  if (entry.source === 'Correction') return <>Weight correction</>;
  if (entry.source === 'Manual') return <>Manual usage</>;
  if (entry.source === 'WriteOff') return <>Finished</>;
  if (entry.source === 'Restore') return <>Restored</>;
  if (!entry.printId) {
    return <span className="text-muted-foreground">Print removed</span>;
  }
  return (
    <span className="flex flex-wrap items-center gap-x-2">
      <Link
        to={`/jobs/${entry.printId}`}
        className="font-medium underline-offset-4 hover:underline"
      >
        {entry.printName || 'Print'}
      </Link>
      {entry.printStatus && (
        <span className="text-xs text-muted-foreground">
          {entry.printStatus}
        </span>
      )}
    </span>
  );
}

/** Every change to a spool's remaining weight, newest first: prints, manual usage, weight corrections, finishing and restoring. */
export function SpoolHistoryCard({ spoolId }: { spoolId: string }) {
  const query = useOpenFarmApiEndpointsSpoolsGetSpoolHistoryEndpoint(spoolId, {
    pageSize: HISTORY_PAGE_SIZE,
  });
  const history = query.data?.data as SpoolHistoryResponse | undefined;
  const entries = history?.entries ?? [];
  const total = history?.totalCount ?? entries.length;

  return (
    <Card>
      <CardHeader>
        <CardTitle>History</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        {query.isLoading ? (
          <Skeleton className="h-24 w-full" />
        ) : query.isError ? (
          <ErrorState
            title="Could not load history"
            onRetry={() => query.refetch()}
          />
        ) : entries.length === 0 ? (
          <p className="text-sm text-muted-foreground">
            No weight changes yet.
          </p>
        ) : (
          <>
            <Table aria-label="Spool history">
              <TableHeader>
                <TableRow>
                  <TableHead>When</TableHead>
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
            {total > entries.length && (
              <p className="text-sm text-muted-foreground">
                Showing latest {entries.length} of {total}.
              </p>
            )}
          </>
        )}
      </CardContent>
    </Card>
  );
}
