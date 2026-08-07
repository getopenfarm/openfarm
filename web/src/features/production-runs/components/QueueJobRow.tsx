import { AlertTriangleIcon } from 'lucide-react';
import { Link } from 'react-router';
import { Badge } from '@/components/ui/badge';
import type { QueueJobRowResponse } from '@/lib/api/generated/endpoints.schemas';
import { JobRemoteControls } from './JobRemoteControls';

const STATUS_VARIANT: Record<
  string,
  'default' | 'secondary' | 'outline' | 'destructive'
> = {
  Queued: 'secondary',
  Printing: 'default',
  Paused: 'outline',
  Completed: 'secondary',
  Failed: 'destructive',
  Cancelled: 'destructive',
};

interface QueueJobRowProps {
  job: QueueJobRowResponse;
}

/** One queue row — status, isBlocked flag, and remote controls. Plain-list shape for US1 (T072); T085 (US2) wraps this in TanStack Virtual, unchanged. */
export function QueueJobRow({ job }: QueueJobRowProps) {
  return (
    <div className="flex items-center gap-3 border-b px-3 py-2 last:border-b-0">
      <div className="flex min-w-0 flex-1 flex-col">
        <span className="truncate text-sm font-medium">{job.partName}</span>
        <Link
          to={`/production-runs/${job.productionRunId}`}
          className="truncate text-xs text-muted-foreground hover:underline"
        >
          {job.runDisplayLabel}
        </Link>
      </div>

      <span className="w-16 shrink-0 text-xs text-muted-foreground">
        ×{job.plateQuantity}
      </span>

      {job.isBlocked && (
        <span
          className="flex shrink-0 items-center gap-1 text-xs text-destructive"
          title="Pinned printer is unavailable"
        >
          <AlertTriangleIcon className="size-3.5" /> Blocked
        </span>
      )}

      <Badge
        variant={
          job.status ? (STATUS_VARIANT[job.status] ?? 'outline') : 'outline'
        }
        className="shrink-0"
      >
        {job.status}
      </Badge>

      <div className="w-20 shrink-0">
        {job.jobId && job.status && (
          <JobRemoteControls jobId={job.jobId} status={job.status} />
        )}
      </div>
    </div>
  );
}
