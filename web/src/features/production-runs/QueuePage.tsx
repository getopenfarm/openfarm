import { ClipboardListIcon, ListIcon } from 'lucide-react';
import { useNavigate } from 'react-router';
import { EmptyState } from '@/components/empty-state/EmptyState';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import type { ListQueueJobsResponse } from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsPrintJobsListQueueJobsEndpoint } from '@/lib/api/generated/print-jobs/print-jobs';
import { QueueJobRow } from './components/QueueJobRow';
import { useProductionHub } from './hooks/useProductionHub';

const PAGE_SIZE = 200;

/**
 * The flat, `QueuePosition`-ordered "dispatch order" queue (FR-008). Plain list for US1 (T072) —
 * T085 (US2) converts this to a TanStack Virtual-backed list once drag-to-reorder lands
 * (constitution Principle V); routed at the pre-existing `/jobs` path (tasks.md T075).
 */
export function QueuePage() {
  const navigate = useNavigate();
  useProductionHub();

  const queueQuery = useOpenFarmApiEndpointsPrintJobsListQueueJobsEndpoint({
    view: 'queue',
    page: 1,
    pageSize: PAGE_SIZE,
  });
  const listData = queueQuery.data?.data as ListQueueJobsResponse | undefined;
  const jobs = listData?.items ?? [];

  const queuedCount = jobs.filter((j) => j.status === 'Queued').length;
  const printingCount = jobs.filter(
    (j) => j.status === 'Printing' || j.status === 'Paused',
  ).length;

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title="Print queue"
        description={`${listData?.totalCount ?? 0} jobs · ${printingCount} printing · ${queuedCount} queued`}
        action={
          <Button
            variant="outline"
            onClick={() => navigate('/production-runs')}
          >
            <ClipboardListIcon /> Production runs
          </Button>
        }
      />

      {queueQuery.isLoading ? (
        <Skeleton className="h-64 w-full" />
      ) : jobs.length === 0 ? (
        <EmptyState
          icon={<ListIcon className="size-8 text-muted-foreground" />}
          title="No jobs in the queue"
          description="Queue a production run to see its jobs here."
        />
      ) : (
        <div className="rounded-md border">
          {jobs.map((job) => (
            <QueueJobRow key={job.jobId} job={job} />
          ))}
        </div>
      )}
    </div>
  );
}
