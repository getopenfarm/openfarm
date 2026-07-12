import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { useOpenFarmApiEndpointsActivityLogGetActivityLogEndpoint } from '@/lib/api/generated/activity-log/activity-log';

const PAGE_SIZE = 25;

export function ActivityLogPage() {
  const [page, setPage] = useState(1);

  const query = useOpenFarmApiEndpointsActivityLogGetActivityLogEndpoint({
    page,
    pageSize: PAGE_SIZE,
  });

  const successData = query.data?.status === 200 ? query.data.data : undefined;
  const items = successData?.items ?? [];
  const totalCount = successData?.totalCount ?? 0;
  const hasNextPage = page * PAGE_SIZE < totalCount;

  return (
    <div className="flex flex-col gap-4">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Entity</TableHead>
            <TableHead>Event</TableHead>
            <TableHead>Actor</TableHead>
            <TableHead>Timestamp</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {query.isLoading
            ? Array.from({ length: 3 }).map((_, index) => (
                // biome-ignore lint/suspicious/noArrayIndexKey: static skeleton placeholder rows
                <TableRow key={`skeleton-${index}`}>
                  <TableCell>
                    <Skeleton className="h-4 w-40" />
                  </TableCell>
                  <TableCell>
                    <Skeleton className="h-4 w-16" />
                  </TableCell>
                  <TableCell>
                    <Skeleton className="h-4 w-32" />
                  </TableCell>
                  <TableCell>
                    <Skeleton className="h-4 w-24" />
                  </TableCell>
                </TableRow>
              ))
            : items.map((entry) => (
                <TableRow key={entry.id}>
                  <TableCell>
                    {entry.entityType} {entry.entityId}
                  </TableCell>
                  <TableCell>{entry.eventType}</TableCell>
                  <TableCell>{entry.actorDisplayName ?? 'System'}</TableCell>
                  <TableCell>
                    {entry.timestampUtc
                      ? new Date(entry.timestampUtc).toLocaleString()
                      : ''}
                  </TableCell>
                </TableRow>
              ))}
        </TableBody>
      </Table>
      <div className="flex items-center justify-between">
        <span className="text-muted-foreground text-sm">
          Page {page} · {totalCount} entries
        </span>
        <div className="flex gap-2">
          <Button
            variant="outline"
            disabled={page <= 1}
            onClick={() => setPage((p) => Math.max(1, p - 1))}
          >
            Previous
          </Button>
          <Button
            variant="outline"
            disabled={!hasNextPage}
            onClick={() => setPage((p) => p + 1)}
          >
            Next
          </Button>
        </div>
      </div>
    </div>
  );
}
