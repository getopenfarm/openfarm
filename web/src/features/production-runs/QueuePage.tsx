/** biome-ignore-all lint/a11y/useSemanticElements: ARIA table elements allow independently scrolling, virtualized row groups with a shared column header. */
/** biome-ignore-all lint/a11y/useFocusableInteractive: This is a static table, not an interactive grid; links and action buttons provide keyboard access. */
import { useQueryClient } from '@tanstack/react-query';
import { ClipboardListIcon, GripVerticalIcon } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { Link, useNavigate } from 'react-router';
import { toast } from 'sonner';
import { DataTableSearch } from '@/components/data-table/DataTableSearch';
import {
  DataTableTabsList,
  DataTableToolbar,
} from '@/components/data-table/DataTableToolbar';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { Tabs, TabsContent, TabsTrigger } from '@/components/ui/tabs';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import type {
  ListQueueJobsResponse,
  QueueJobRowResponse,
} from '@/lib/api/generated/endpoints.schemas';
import {
  getOpenFarmApiEndpointsPrintJobsListQueueJobsEndpointQueryKey,
  useOpenFarmApiEndpointsPrintJobsListQueueJobsEndpoint,
  useOpenFarmApiEndpointsPrintJobsReorderPrintJobEndpoint,
} from '@/lib/api/generated/print-jobs/print-jobs';
import { useOpenFarmApiEndpointsPrintersListFleetEndpoint } from '@/lib/api/generated/printers/printers';
import { cn } from '@/lib/utils';
import { JobMatchingDialog } from './components/JobMatchingDialog';
import {
  QueueJobActions,
  type RemoteAction,
} from './components/QueueJobActions';
import {
  QUEUE_COLUMNS,
  QueueTableCell,
  QueueTableSection,
} from './components/QueueTableSection';
import { useProductionHub } from './hooks/useProductionHub';

const STATUSES = [
  'Active',
  'Queued',
  'Printing',
  'Paused',
  'Completed',
  'Failed',
  'Cancelled',
] as const;
const GROUPS = ['queue', 'backfill'] as const;
type Group = (typeof GROUPS)[number];
const LABELS = { queue: 'Finite jobs', backfill: 'Backfill jobs' };
/** One virtualized table, with independently paged finite and backfill sections. */
export function QueuePage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const finiteScrollRef = useRef<HTMLDivElement>(null);
  const backfillScrollRef = useRef<HTMLDivElement>(null);
  const [status, setStatus] = useState<string>('Active');
  const [search, setSearch] = useState('');
  const [backfillCollapsedOverride, setBackfillCollapsedOverride] = useState<
    boolean | null
  >(null);
  const [pageSizes, setPageSizes] = useState({ queue: 50, backfill: 50 });
  const [pages, setPages] = useState({ queue: 1, backfill: 1 });
  const [dragged, setDragged] = useState<{
    jobId: string;
    group: Group;
  } | null>(null);
  const [pending, setPending] = useState<
    Record<string, { action: RemoteAction; status: string }>
  >({});
  useProductionHub((jobId, observedStatus) =>
    setPending((previous) => {
      if (!previous[jobId] || previous[jobId].status === observedStatus)
        return previous;
      const next = { ...previous };
      delete next[jobId];
      return next;
    }),
  );
  const finiteQuery = useOpenFarmApiEndpointsPrintJobsListQueueJobsEndpoint({
    view: 'queue',
    page: pages.queue,
    pageSize: pageSizes.queue,
    status,
    search,
  });
  const backfillQuery = useOpenFarmApiEndpointsPrintJobsListQueueJobsEndpoint({
    view: 'backfill',
    page: pages.backfill,
    pageSize: pageSizes.backfill,
    status,
    search,
  });
  const queries = { queue: finiteQuery, backfill: backfillQuery };
  const finiteData = finiteQuery.data?.data as
    | ListQueueJobsResponse
    | undefined;
  const backfillData = backfillQuery.data?.data as
    | ListQueueJobsResponse
    | undefined;
  const dataByGroup = { queue: finiteData, backfill: backfillData };
  const backfillCollapsed =
    backfillCollapsedOverride ?? (backfillData?.totalCount ?? 0) === 0;
  const printers =
    useOpenFarmApiEndpointsPrintersListFleetEndpoint().data?.data?.printers ??
    [];
  const canReorder = status === 'Queued' && !search.trim();
  const reorder = useOpenFarmApiEndpointsPrintJobsReorderPrintJobEndpoint({
    mutation: {
      onSuccess: () =>
        queryClient.invalidateQueries({
          queryKey:
            getOpenFarmApiEndpointsPrintJobsListQueueJobsEndpointQueryKey(),
        }),
      onError: async (error) =>
        toast.error(await extractErrorDetail(error, 'Could not reorder job.')),
    },
  });

  // A removal or live transition can invalidate the last page in either section.
  useEffect(() => {
    setPages((previous) => {
      const queue = finiteData
        ? Math.min(
            previous.queue,
            Math.max(
              1,
              Math.ceil((finiteData?.totalCount ?? 0) / pageSizes.queue),
            ),
          )
        : previous.queue;
      const backfill = backfillData
        ? Math.min(
            previous.backfill,
            Math.max(
              1,
              Math.ceil((backfillData?.totalCount ?? 0) / pageSizes.backfill),
            ),
          )
        : previous.backfill;
      return queue === previous.queue && backfill === previous.backfill
        ? previous
        : { queue, backfill };
    });
  }, [finiteData, backfillData, pageSizes]);

  function resetFilters(nextStatus: string, nextSearch: string) {
    setStatus(nextStatus);
    setSearch(nextSearch);
    setPages({ queue: 1, backfill: 1 });
    setDragged(null);
    finiteScrollRef.current?.scrollTo?.(0, 0);
    backfillScrollRef.current?.scrollTo?.(0, 0);
  }
  function count(value: string) {
    if (
      finiteQuery.isError ||
      backfillQuery.isError ||
      !finiteQuery.data ||
      !backfillData
    )
      return '—';
    const statuses =
      value === 'Active' ? ['Queued', 'Printing', 'Paused'] : [value];
    return GROUPS.reduce(
      (total, group) =>
        total +
        statuses.reduce(
          (sum, s) => sum + (dataByGroup[group]?.statusCounts?.[s] ?? 0),
          0,
        ),
      0,
    );
  }
  function move(jobId: string, afterJobId: string | null) {
    if (canReorder && !reorder.isPending)
      reorder.mutate({ id: jobId, data: { afterJobId } });
  }
  function pendingAction(job: QueueJobRowResponse) {
    const request = pending[job.jobId ?? ''];
    return request?.status === job.status ? request.action : null;
  }

  return (
    <Page className="min-h-0 flex-1">
      <PageHeader
        title="Print queue"
        description="Finite and backfill jobs · status updates live."
        action={
          <Button
            variant="outline"
            onClick={() => navigate('/production-runs')}
          >
            <ClipboardListIcon data-icon="inline-start" />
            Production runs
          </Button>
        }
      />
      <div className="flex min-h-0 flex-1 flex-col gap-4">
        <Tabs
          className="min-h-0 flex-1 gap-4"
          value={status}
          onValueChange={(value) => resetFilters(value, search)}
        >
          <DataTableToolbar
            search={
              <DataTableSearch
                label="Search jobs by part name"
                placeholder="Search jobs by part name…"
                value={search}
                onValueChange={(value) => resetFilters(status, value)}
              />
            }
          >
            <DataTableTabsList aria-label="Job status">
              {STATUSES.map((value) => (
                <TabsTrigger key={value} value={value}>
                  {value}{' '}
                  <span className="ml-1 tabular-nums opacity-70">
                    {count(value)}
                  </span>
                </TabsTrigger>
              ))}
            </DataTableTabsList>
          </DataTableToolbar>
          <TabsContent
            value={status}
            className="flex min-h-0 flex-1 flex-col gap-4"
          >
            <div className="flex min-h-0 flex-1 flex-col overflow-x-auto overflow-y-hidden rounded-md border">
              <div
                role="table"
                aria-label="Print queue"
                aria-colcount={6}
                className="flex min-h-0 min-w-[900px] flex-1 flex-col text-sm"
              >
                <div role="rowgroup" className="shrink-0 bg-muted">
                  <div
                    role="row"
                    className="grid h-10"
                    style={{ gridTemplateColumns: QUEUE_COLUMNS }}
                  >
                    {[
                      'Job',
                      'Units',
                      'Printer',
                      'Status',
                      'Readiness',
                      'Actions',
                    ].map((label) => (
                      <div
                        role="columnheader"
                        key={label}
                        className="flex items-center px-2 font-medium"
                      >
                        {label}
                      </div>
                    ))}
                  </div>
                </div>
                <div
                  className={cn(
                    'grid min-h-0 flex-1',
                    backfillCollapsed
                      ? 'grid-rows-[minmax(0,1fr)_auto]'
                      : 'grid-rows-[minmax(0,7fr)_minmax(0,3fr)]',
                  )}
                >
                  {GROUPS.map((group) => {
                    const query = queries[group];
                    const data = dataByGroup[group];
                    return (
                      <QueueTableSection
                        key={group}
                        label={LABELS[group]}
                        collapsed={group === 'backfill' && backfillCollapsed}
                        onToggle={
                          group === 'backfill'
                            ? () =>
                                setBackfillCollapsedOverride(!backfillCollapsed)
                            : undefined
                        }
                        description={
                          group === 'queue'
                            ? 'Fixed production quantity'
                            : 'Unlimited production demand'
                        }
                        jobs={data?.items ?? []}
                        totalCount={data?.totalCount ?? 0}
                        page={pages[group]}
                        pageSize={pageSizes[group]}
                        onPageSizeChange={(size) => {
                          setPageSizes((previous) => ({
                            ...previous,
                            [group]: size,
                          }));
                          setPages((previous) => ({ ...previous, [group]: 1 }));
                          (group === 'queue'
                            ? finiteScrollRef
                            : backfillScrollRef
                          ).current?.scrollTo?.(0, 0);
                        }}
                        isFetching={query.isFetching}
                        isError={query.isError}
                        isLoading={query.isLoading}
                        scrollRef={
                          group === 'queue'
                            ? finiteScrollRef
                            : backfillScrollRef
                        }
                        onPageChange={(page) => {
                          setPages((previous) => ({
                            ...previous,
                            [group]: page,
                          }));
                          (group === 'queue'
                            ? finiteScrollRef
                            : backfillScrollRef
                          ).current?.scrollTo?.(0, 0);
                        }}
                        feedback={
                          query.isLoading ? (
                            <Skeleton className="h-32 w-full" />
                          ) : query.isError ? (
                            <ErrorState
                              bordered={false}
                              size="sm"
                              title={`Could not load ${LABELS[group].toLowerCase()}`}
                              onRetry={() => query.refetch()}
                            />
                          ) : (
                            <EmptyState
                              bordered={false}
                              size="sm"
                              title={
                                search
                                  ? 'No jobs match search'
                                  : `No ${status.toLowerCase()} ${LABELS[group].toLowerCase()}`
                              }
                              description="Try another status or queue a production run."
                            />
                          )
                        }
                        rowProps={(job) => ({
                          draggable:
                            canReorder &&
                            job.status === 'Queued' &&
                            !job.dispatchedPrinterId &&
                            !reorder.isPending,
                          onDragStart: () =>
                            job.jobId &&
                            setDragged({ jobId: job.jobId, group }),
                          onDragEnd: () => setDragged(null),
                          onDragOver: (event) => {
                            if (
                              job.status === 'Queued' &&
                              !job.dispatchedPrinterId &&
                              dragged?.group === group &&
                              canReorder
                            )
                              event.preventDefault();
                          },
                          onDrop: (event) => {
                            event.preventDefault();
                            if (
                              job.jobId &&
                              !job.dispatchedPrinterId &&
                              dragged?.group === group &&
                              dragged.jobId !== job.jobId
                            )
                              move(dragged.jobId, job.jobId);
                            setDragged(null);
                          },
                        })}
                        renderRow={(job) => {
                          const request = pendingAction(job);
                          const printerId =
                            job.dispatchedPrinterId ?? job.pinnedPrinterId;
                          const printer = printers.find(
                            (p) => p.id === printerId,
                          );
                          return (
                            <>
                              <QueueTableCell className="flex items-center gap-2 whitespace-normal">
                                {canReorder && !job.dispatchedPrinterId && (
                                  <GripVerticalIcon
                                    aria-hidden
                                    className="size-4 shrink-0 text-muted-foreground"
                                  />
                                )}
                                <div className="min-w-0">
                                  <Link
                                    to={`/jobs/${job.jobId}`}
                                    className="font-medium hover:underline"
                                  >
                                    {job.partName}
                                  </Link>
                                  <Link
                                    to={`/production-runs/${job.productionRunId}`}
                                    className="mt-0.5 block text-xs text-muted-foreground hover:underline"
                                  >
                                    {job.runDisplayLabel}
                                  </Link>
                                </div>
                              </QueueTableCell>
                              <QueueTableCell className="flex items-center tabular-nums">
                                {job.plateQuantity ?? '—'}
                              </QueueTableCell>
                              <QueueTableCell className="flex flex-col justify-center whitespace-normal">
                                {printerId ? (
                                  <Link
                                    className="hover:underline"
                                    to={`/printers/${printerId}`}
                                  >
                                    {printer?.name ?? 'Unknown printer'}
                                  </Link>
                                ) : (
                                  <span className="text-muted-foreground">
                                    Automatic assignment
                                  </span>
                                )}
                                {job.pinnedPrinterId &&
                                  !job.dispatchedPrinterId && (
                                    <span className="text-xs text-muted-foreground">
                                      Pinned
                                    </span>
                                  )}
                              </QueueTableCell>
                              <QueueTableCell className="flex flex-col items-start justify-center gap-1 whitespace-normal">
                                <Badge
                                  variant={
                                    job.status === 'Failed'
                                      ? 'destructive'
                                      : job.status === 'Printing'
                                        ? 'default'
                                        : 'secondary'
                                  }
                                >
                                  {job.status}
                                </Badge>
                                {request && (
                                  <span
                                    role="status"
                                    className="text-xs text-muted-foreground"
                                  >
                                    {request === 'pause'
                                      ? 'Pause'
                                      : request === 'resume'
                                        ? 'Resume'
                                        : 'Cancel'}{' '}
                                    requested…
                                  </span>
                                )}
                              </QueueTableCell>
                              <QueueTableCell className="flex items-center whitespace-normal">
                                <JobMatchingDialog
                                  jobId={job.jobId ?? ''}
                                  trigger={
                                    <Button
                                      variant="link"
                                      size="sm"
                                      className={cn(
                                        'h-auto justify-start px-0 text-left whitespace-normal',
                                        job.isBlocked ? 'text-destructive' : '',
                                      )}
                                      title={job.blockingReason ?? undefined}
                                    >
                                      {job.isBlocked
                                        ? 'No matching printer'
                                        : job.status === 'Queued'
                                          ? job.dispatchedPrinterId
                                            ? 'Dispatching…'
                                            : 'Matching printer available'
                                          : 'Job diagnosis'}
                                    </Button>
                                  }
                                />
                              </QueueTableCell>
                              <QueueTableCell className="flex items-center justify-end">
                                <QueueJobActions
                                  job={job}
                                  printers={printers}
                                  onMoveToTop={
                                    canReorder && !reorder.isPending
                                      ? () => job.jobId && move(job.jobId, null)
                                      : undefined
                                  }
                                  pending={request}
                                  onPendingChange={(action) =>
                                    job.jobId &&
                                    setPending((previous) => ({
                                      ...previous,
                                      [job.jobId ?? '']: {
                                        action,
                                        status: job.status ?? '',
                                      },
                                    }))
                                  }
                                />
                              </QueueTableCell>
                            </>
                          );
                        }}
                      />
                    );
                  })}
                </div>
              </div>
            </div>
          </TabsContent>
        </Tabs>
      </div>
    </Page>
  );
}
