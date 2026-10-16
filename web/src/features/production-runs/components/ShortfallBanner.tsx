import { useQueryClient } from '@tanstack/react-query';
import { AlertTriangleIcon } from 'lucide-react';
import { Link } from 'react-router';
import { toast } from 'sonner';
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Spinner } from '@/components/ui/spinner';
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
  const jobsBlockedForRequeue = requirement.jobsBlockedForRequeue ?? [];
  if (jobsNeedingRequeue.length === 0 && jobsBlockedForRequeue.length === 0) {
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
      const detail = await extractErrorDetail(error, 'Could not requeue job.');
      toast.error(detail);
    }
  }

  return (
    <Alert variant="destructive">
      <AlertTriangleIcon />
      <AlertTitle>{requirement.partName}: production shortfall</AlertTitle>
      <AlertDescription className="flex flex-col gap-2">
        {jobsNeedingRequeue.length > 0 && (
          <>
            <span>
              {jobsNeedingRequeue.length} job
              {jobsNeedingRequeue.length === 1 ? '' : 's'} need requeueing
            </span>
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
                  {requeueMutation.isPending && (
                    <Spinner data-icon="inline-start" />
                  )}
                  Requeue job {jobId.slice(0, 8)}
                </Button>
              ))}
            </div>
          </>
        )}
        {jobsBlockedForRequeue.length > 0 && (
          <>
            <span>
              {jobsBlockedForRequeue.length} job
              {jobsBlockedForRequeue.length === 1 ? '' : 's'} cannot be requeued
              because verified production inputs are unavailable.
            </span>
            <div className="flex flex-wrap gap-2">
              {jobsBlockedForRequeue.map((jobId) => (
                <Link key={jobId} to={`/jobs/${jobId}`}>
                  View job {jobId.slice(0, 8)}
                </Link>
              ))}
            </div>
          </>
        )}
      </AlertDescription>
    </Alert>
  );
}
