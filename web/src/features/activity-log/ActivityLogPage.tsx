import { useState } from 'react';
import { BADGE_KIND_META } from '@/components/status-badge/kinds';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { useOpenFarmApiEndpointsActivityLogGetActivityLogEndpoint } from '@/lib/api/generated/activity-log/activity-log';
import { formatRelativeTime } from '@/lib/formatRelativeTime';
import { cn } from '@/lib/utils';
import { activityEventKind, describeActivityEvent } from './lib/activityEvent';

const PAGE_SIZE = 25;
const SKELETON_ROW_IDS = ['a', 'b', 'c', 'd'];

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
      <div>
        <h2 className="font-heading text-2xl font-semibold">Activity log</h2>
        <p className="text-sm text-muted-foreground">
          Every status change, job update, and profile edit — the shared audit
          trail.
        </p>
      </div>

      <div className="overflow-hidden rounded-xl ring-1 ring-foreground/10">
        {query.isLoading ? (
          <div className="flex flex-col">
            {SKELETON_ROW_IDS.map((rowId) => (
              <div
                key={rowId}
                className="flex items-center gap-3 border-b p-4 last:border-b-0"
              >
                <Skeleton className="size-2.5 shrink-0 rounded-full" />
                <div className="flex-1 space-y-1.5">
                  <Skeleton className="h-4 w-64" />
                  <Skeleton className="h-3 w-40" />
                </div>
              </div>
            ))}
          </div>
        ) : query.isError ? (
          <div className="flex flex-col items-center gap-2 p-12 text-center">
            <p className="text-sm text-muted-foreground">
              Couldn't load the activity log.
            </p>
            <Button variant="outline" onClick={() => query.refetch()}>
              Retry
            </Button>
          </div>
        ) : items.length === 0 ? (
          <div className="p-12 text-center text-sm text-muted-foreground">
            No activity recorded yet.
          </div>
        ) : (
          <div className="flex flex-col">
            {items.map((entry) => {
              const kind = activityEventKind(entry.eventType ?? '');
              const meta = BADGE_KIND_META[kind];
              const relativeTime = formatRelativeTime(entry.timestampUtc);
              return (
                <div
                  key={entry.id}
                  className="flex items-center gap-3 border-b p-4 last:border-b-0"
                >
                  <span
                    className={cn(
                      'size-2.5 shrink-0 rounded-full ring-4',
                      meta.dotClass,
                    )}
                    style={{
                      boxShadow: `0 0 0 4px color-mix(in oklch, var(--kind-${kind}) 15%, transparent)`,
                    }}
                  />
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-sm font-medium">
                      {describeActivityEvent(
                        entry.entityType ?? 'Item',
                        entry.eventType ?? 'Updated',
                      )}
                    </p>
                    <p className="truncate text-xs text-muted-foreground">
                      {entry.actorDisplayName ?? 'System'} · {relativeTime}
                    </p>
                  </div>
                  <span className="shrink-0 font-mono text-xs text-muted-foreground">
                    {relativeTime}
                  </span>
                </div>
              );
            })}
          </div>
        )}
      </div>

      <div className="flex items-center justify-between">
        <span className="text-sm text-muted-foreground">
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
