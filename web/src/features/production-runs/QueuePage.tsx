import { useQueryClient } from '@tanstack/react-query';
import { useVirtualizer } from '@tanstack/react-virtual';
import { ClipboardListIcon, ListIcon } from 'lucide-react';
import { useRef, useState } from 'react';
import { useNavigate } from 'react-router';
import { toast } from 'sonner';
import { EmptyState } from '@/components/empty-state/EmptyState';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import type { ListQueueJobsResponse } from '@/lib/api/generated/endpoints.schemas';
import {
  getOpenFarmApiEndpointsPrintJobsListQueueJobsEndpointQueryKey,
  useOpenFarmApiEndpointsPrintJobsListQueueJobsEndpoint,
  useOpenFarmApiEndpointsPrintJobsReorderPrintJobEndpoint,
} from '@/lib/api/generated/print-jobs/print-jobs';
import { QueueJobRow } from './components/QueueJobRow';
import { useProductionHub } from './hooks/useProductionHub';

const PAGE_SIZE = 200;
const ROW_HEIGHT = 56;

/**
 * The flat, `QueuePosition`-ordered "dispatch order" queue (FR-008) — TanStack Virtual-backed
 * (constitution Principle V, this codebase's first real consumer, tasks.md T085) with native HTML5
 * drag-and-drop reordering: dropping a row onto another places it immediately after that row
 * (contracts/production-run-queue-api.md's `afterJobId` semantic). Routed at the pre-existing
 * `/jobs` path (tasks.md T075).
 */
export function QueuePage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  useProductionHub();

  const parentRef = useRef<HTMLDivElement>(null);
  const [page, setPage] = useState(1);
  const [view, setView] = useState<'queue' | 'backfill'>('queue');
  const [draggedIndex, setDraggedIndex] = useState<number | null>(null);

  const queueQuery = useOpenFarmApiEndpointsPrintJobsListQueueJobsEndpoint({
    view,
    page,
    pageSize: PAGE_SIZE,
  });
  const listData = queueQuery.data?.data as ListQueueJobsResponse | undefined;
  const jobs = listData?.items ?? [];

  const virtualizer = useVirtualizer({
    count: jobs.length,
    getScrollElement: () => parentRef.current,
    estimateSize: () => ROW_HEIGHT,
    overscan: 8,
  });

  const reorderMutation =
    useOpenFarmApiEndpointsPrintJobsReorderPrintJobEndpoint({
      mutation: {
        onSuccess: () => {
          queryClient.invalidateQueries({
            queryKey:
              getOpenFarmApiEndpointsPrintJobsListQueueJobsEndpointQueryKey(),
          });
        },
        onError: async (error) => {
          toast.error(
            await extractErrorDetail(error, 'Could not reorder this job.'),
          );
        },
      },
    });

  function handleDrop(dropIndex: number) {
    const fromIndex = draggedIndex;
    setDraggedIndex(null);
    if (fromIndex === null || fromIndex === dropIndex) {
      return;
    }

    const draggedJob = jobs[fromIndex];
    const targetJob = jobs[dropIndex];
    if (!draggedJob?.jobId) {
      return;
    }

    reorderMutation.mutate({
      id: draggedJob.jobId,
      data: { afterJobId: targetJob?.jobId ?? null },
    });
  }

  const queuedCount = jobs.filter((j) => j.status === 'Queued').length;
  const printingCount = jobs.filter(
    (j) => j.status === 'Printing' || j.status === 'Paused',
  ).length;

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title="Print queue"
        description={`${listData?.totalCount ?? 0} jobs · ${printingCount} printing and ${queuedCount} queued on this page`}
        action={
          <Button
            variant="outline"
            onClick={() => navigate('/production-runs')}
          >
            <ClipboardListIcon /> Production runs
          </Button>
        }
      />

      <div className="flex items-center justify-between gap-2">
        <div className="flex gap-2">
          <Button
            variant={view === 'queue' ? 'default' : 'outline'}
            onClick={() => {
              setView('queue');
              setPage(1);
            }}
          >
            Finite jobs
          </Button>
          <Button
            variant={view === 'backfill' ? 'default' : 'outline'}
            onClick={() => {
              setView('backfill');
              setPage(1);
            }}
          >
            Backfill jobs
          </Button>
        </div>
        <div className="flex items-center gap-2">
          <Button
            variant="outline"
            disabled={page === 1}
            onClick={() => {
              setPage(page - 1);
              parentRef.current?.scrollTo?.(0, 0);
            }}
          >
            Previous
          </Button>
          <span className="text-sm">
            Page {page} of{' '}
            {Math.max(1, Math.ceil((listData?.totalCount ?? 0) / PAGE_SIZE))}
          </span>
          <Button
            variant="outline"
            disabled={page * PAGE_SIZE >= (listData?.totalCount ?? 0)}
            onClick={() => {
              setPage(page + 1);
              parentRef.current?.scrollTo?.(0, 0);
            }}
          >
            Next
          </Button>
        </div>
      </div>
      {queueQuery.isLoading ? (
        <Skeleton className="h-64 w-full" />
      ) : jobs.length === 0 ? (
        <EmptyState
          icon={<ListIcon className="size-8 text-muted-foreground" />}
          title="No jobs in the queue"
          description="Queue a production run to see its jobs here."
        />
      ) : (
        <div
          ref={parentRef}
          className="max-h-[70vh] overflow-auto rounded-md border"
        >
          <div
            style={{
              height: virtualizer.getTotalSize(),
              position: 'relative',
            }}
          >
            {virtualizer.getVirtualItems().map((virtualRow) => {
              const job = jobs[virtualRow.index];
              if (!job) {
                return null;
              }

              return (
                <div
                  key={job.jobId}
                  style={{
                    position: 'absolute',
                    top: 0,
                    left: 0,
                    width: '100%',
                    transform: `translateY(${virtualRow.start}px)`,
                    height: virtualRow.size,
                  }}
                >
                  <QueueJobRow
                    job={job}
                    draggable={job.status === 'Queued'}
                    onDragStart={() => setDraggedIndex(virtualRow.index)}
                    onDragOver={(e) => e.preventDefault()}
                    onDrop={() => handleDrop(virtualRow.index)}
                  />
                </div>
              );
            })}
          </div>
        </div>
      )}
    </div>
  );
}
