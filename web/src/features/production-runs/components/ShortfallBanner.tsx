import { useQueryClient } from '@tanstack/react-query';
import { AlertTriangleIcon } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import type { PartRequirementSummaryResponse } from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsPrintJobsRequeuePrintJobEndpoint } from '@/lib/api/generated/print-jobs/print-jobs';
import { getOpenFarmApiEndpointsProductionRunsGetProductionRunEndpointQueryKey } from '@/lib/api/generated/production-runs/production-runs';

interface ShortfallBannerProps {
  runId: string;
  requirement: PartRequirementSummaryResponse;
}

/** Renders a requirement's unrequeued shortfall with a one-click Requeue action per job — research.md R13, tasks.md T069. */
export function ShortfallBanner({ runId, requirement }: ShortfallBannerProps) {
  const queryClient = useQueryClient();
  const requeueMutation =
    useOpenFarmApiEndpointsPrintJobsRequeuePrintJobEndpoint();

  const jobsNeedingRequeue = requirement.jobsNeedingRequeue ?? [];
  if (jobsNeedingRequeue.length === 0) {
    return null;
  }

  async function handleRequeue(jobId: string) {
    try {
      await requeueMutation.mutateAsync({ id: jobId });
      toast.success('Job requeued.');
      queryClient.invalidateQueries({
        queryKey:
          getOpenFarmApiEndpointsProductionRunsGetProductionRunEndpointQueryKey(
            runId,
          ),
      });
    } catch (error) {
      const detail = await extractErrorDetail(
        error,
        'Could not requeue this job.',
      );
      toast.error(detail);
    }
  }

  return (
    <div className="flex flex-col gap-2 rounded-md border border-destructive/30 bg-destructive/5 p-3">
      <div className="flex items-center gap-2 text-sm font-medium text-destructive">
        <AlertTriangleIcon className="size-4" />
        {requirement.partName}: {jobsNeedingRequeue.length} job
        {jobsNeedingRequeue.length === 1 ? '' : 's'} need requeueing
      </div>
      <div className="flex flex-wrap gap-2">
        {jobsNeedingRequeue.map((jobId) => (
          <Button
            key={jobId}
            type="button"
            size="sm"
            variant="outline"
            disabled={requeueMutation.isPending}
            onClick={() => handleRequeue(jobId)}
          >
            Requeue job {jobId.slice(0, 8)}
          </Button>
        ))}
      </div>
    </div>
  );
}
