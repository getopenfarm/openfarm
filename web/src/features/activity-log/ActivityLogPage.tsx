import { HistoryIcon } from 'lucide-react';
import { TablePagination } from '@/components/data-table/TablePagination';
import {
  serverListQueryOptions,
  useRetainedListData,
  useServerListState,
  useServerPageRecovery,
} from '@/components/data-table/useServerListState';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import { BADGE_KIND_META } from '@/components/status-badge/kinds';
import { Separator } from '@/components/ui/separator';
import { Skeleton } from '@/components/ui/skeleton';
import { useOpenFarmApiEndpointsActivityLogGetActivityLogEndpoint } from '@/lib/api/generated/activity-log/activity-log';
import { formatRelativeTime } from '@/lib/formatRelativeTime';
import { cn } from '@/lib/utils';
import { ActivityDetails } from './components/ActivityDetails';
import { activityEventKind, describeActivityEvent } from './lib/activityEvent';

const SKELETON_ROW_IDS = ['a', 'b', 'c', 'd'];

export function ActivityLogPage() {
  const { page, pageSize, setPage, setPageSize } = useServerListState(
    'timestampUtc',
    'timestampUtc',
  );

  const query = useOpenFarmApiEndpointsActivityLogGetActivityLogEndpoint(
    {
      page,
      pageSize,
    },
    { query: serverListQueryOptions },
  );

  const successData = useRetainedListData(
    query.data?.status === 200 ? query.data.data : undefined,
  );
  const items = successData?.items ?? [];
  const totalCount = successData?.totalCount ?? 0;
  useServerPageRecovery(
    page,
    pageSize,
    totalCount,
    query.isSuccess && !query.isFetching && !query.isPlaceholderData,
    setPage,
  );

  return (
    <Page>
      <PageHeader
        title="Activity log"
        description="Status changes, job updates, profile edits."
      />

      <div
        aria-busy={query.isFetching || undefined}
        className="overflow-hidden rounded-md border"
      >
        <span className="sr-only" aria-live="polite">
          {query.isFetching ? 'Loading…' : ''}
        </span>
        {query.isError && successData ? (
          <ErrorState
            description="Could not refresh activity log."
            onRetry={() => query.refetch()}
            bordered={false}
          />
        ) : null}
        {query.isLoading ? (
          <div className="flex flex-col">
            {SKELETON_ROW_IDS.map((rowId) => (
              <div
                key={rowId}
                className="flex items-center gap-3 border-b p-4 last:border-b-0"
              >
                <Skeleton className="size-2.5 shrink-0 rounded-full" />
                <div className="flex-1 flex flex-col gap-1.5">
                  <Skeleton className="h-4 w-64" />
                  <Skeleton className="h-3 w-40" />
                </div>
              </div>
            ))}
          </div>
        ) : query.isError && !successData ? (
          <ErrorState
            description="Could not load activity log."
            onRetry={() => query.refetch()}
            bordered={false}
          />
        ) : items.length === 0 ? (
          <EmptyState
            icon={<HistoryIcon className="size-8 text-muted-foreground" />}
            title="No activity"
            bordered={false}
          />
        ) : (
          <div className="flex flex-col">
            {items.map((entry) => {
              const kind = activityEventKind(
                entry.eventType ?? '',
                entry.details,
              );
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
                      meta.ringClass,
                    )}
                  />
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-sm font-medium">
                      {describeActivityEvent(
                        entry.entityType ?? 'Item',
                        entry.eventType ?? 'Updated',
                        entry.details,
                      )}
                    </p>
                    <ActivityDetails details={entry.details} />
                    <p className="truncate text-xs text-muted-foreground">
                      {entry.actorDisplayName ?? 'System'}
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
        <Separator />
        <TablePagination
          label="Activity log"
          page={page}
          pageSize={pageSize}
          totalCount={totalCount}
          disabled={
            query.isFetching || query.isError || query.isPlaceholderData
          }
          unavailable={query.isError && !successData}
          onPageChange={setPage}
          onPageSizeChange={setPageSize}
        />
      </div>
    </Page>
  );
}
