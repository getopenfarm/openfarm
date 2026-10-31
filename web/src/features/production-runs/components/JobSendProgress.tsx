import { skipToken, useQuery } from '@tanstack/react-query';
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';
import { Progress } from '@/components/ui/progress';
import {
  type PrintJobSendProgress,
  sendProgressKey,
} from '../hooks/sendProgress';

function useJobSendProgress(jobId: string) {
  return useQuery<PrintJobSendProgress>({
    queryKey: sendProgressKey(jobId),
    queryFn: skipToken,
  }).data;
}

function label({ stage, percent, attempt }: PrintJobSendProgress) {
  const retry = attempt > 1 ? ` · attempt ${attempt}` : '';
  switch (stage) {
    case 'Slicing':
      return 'Slicing…';
    case 'Uploading':
      return `Uploading ${percent ?? 0}%${retry}`;
    case 'Starting':
      return `Starting print…${retry}`;
    case 'Retrying':
      return `Send failed · retrying (attempt ${attempt})`;
    default:
      return 'Sent · waiting for printer';
  }
}

/** The live send stage of a job, or `fallback` when nothing is being sent. */
export function JobSendLabel({
  jobId,
  fallback,
}: {
  jobId: string;
  fallback: string;
}) {
  const progress = useJobSendProgress(jobId);
  return progress ? label(progress) : fallback;
}

/** The upload bar, or why the last send attempt failed. */
export function JobSendDetail({ jobId }: { jobId: string }) {
  const progress = useJobSendProgress(jobId);
  if (progress?.stage === 'Uploading')
    return (
      <Progress
        value={progress.percent ?? 0}
        className="w-32"
        aria-label="Upload progress"
      />
    );
  if (progress?.stage === 'Retrying' && progress.error)
    return (
      <span
        className="line-clamp-2 text-destructive text-xs"
        title={progress.error}
      >
        {progress.error}
      </span>
    );
  return null;
}

/** The live send stage as a page alert; nothing when the job is not being sent. */
export function JobSendAlert({ jobId }: { jobId: string }) {
  const progress = useJobSendProgress(jobId);
  if (!progress) return null;
  return (
    <Alert id="job-send-progress" aria-live="polite">
      <AlertTitle>{label(progress)}</AlertTitle>
      <AlertDescription>
        <JobSendDetail jobId={jobId} />
      </AlertDescription>
    </Alert>
  );
}
