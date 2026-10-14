import { PauseIcon, PlayIcon, XIcon } from 'lucide-react';
import { useEffect, useState } from 'react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import {
  DropdownMenuItem,
  DropdownMenuSeparator,
} from '@/components/ui/dropdown-menu';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import {
  useOpenFarmApiEndpointsPrintJobsCancelPrintJobEndpoint,
  useOpenFarmApiEndpointsPrintJobsPausePrintJobEndpoint,
  useOpenFarmApiEndpointsPrintJobsResumePrintJobEndpoint,
} from '@/lib/api/generated/print-jobs/print-jobs';

type PendingAction = 'pause' | 'resume' | 'cancel' | null;

interface JobRemoteControlsProps {
  jobId: string;
  status: string;
  menu?: boolean;
  pending?: PendingAction;
  onPendingChange?: (action: PendingAction) => void;
}

/**
 * Pause/Resume/Cancel for a Printing/Paused job. Deliberately not optimistic (research.md R12,
 * FR-024a): a click shows a "requested, awaiting confirmation" state until the job's `status`
 * prop (driven by the FarmHub push -> query invalidation -> refetch) actually changes.
 */
export function JobRemoteControls({
  jobId,
  status,
  menu = false,
  pending,
  onPendingChange,
}: JobRemoteControlsProps) {
  const [localPending, setLocalPending] = useState<PendingAction>(null);
  const pendingAction = pending === undefined ? localPending : pending;
  function setPendingAction(action: PendingAction) {
    setLocalPending(action);
    onPendingChange?.(action);
  }

  // biome-ignore lint/correctness/useExhaustiveDependencies: clears the pending flag once the server's own reported status (fed back through this prop) actually moves — not on every render.
  useEffect(() => {
    setLocalPending(null);
  }, [status]);

  const pauseMutation = useOpenFarmApiEndpointsPrintJobsPausePrintJobEndpoint();
  const resumeMutation =
    useOpenFarmApiEndpointsPrintJobsResumePrintJobEndpoint();
  const cancelMutation =
    useOpenFarmApiEndpointsPrintJobsCancelPrintJobEndpoint();

  async function handlePause() {
    setPendingAction('pause');
    try {
      await pauseMutation.mutateAsync({ id: jobId });
    } catch (error) {
      setPendingAction(null);
      toast.error(await extractErrorDetail(error, 'Could not pause this job.'));
    }
  }

  async function handleResume() {
    setPendingAction('resume');
    try {
      await resumeMutation.mutateAsync({ id: jobId });
    } catch (error) {
      setPendingAction(null);
      toast.error(
        await extractErrorDetail(error, 'Could not resume this job.'),
      );
    }
  }

  async function handleCancel() {
    setPendingAction('cancel');
    try {
      await cancelMutation.mutateAsync({ id: jobId });
    } catch (error) {
      setPendingAction(null);
      toast.error(
        await extractErrorDetail(error, 'Could not cancel this job.'),
      );
    }
  }

  if (status !== 'Printing' && status !== 'Paused') {
    return null;
  }

  if (pendingAction) {
    const label =
      pendingAction === 'pause'
        ? 'Pause requested…'
        : pendingAction === 'resume'
          ? 'Resume requested…'
          : 'Cancel requested…';
    return menu ? (
      <DropdownMenuItem disabled>{label}</DropdownMenuItem>
    ) : (
      <span className="text-xs text-muted-foreground">{label}</span>
    );
  }

  if (menu)
    return (
      <>
        {status === 'Printing' ? (
          <DropdownMenuItem onSelect={handlePause}>
            <PauseIcon />
            Pause
          </DropdownMenuItem>
        ) : (
          <DropdownMenuItem onSelect={handleResume}>
            <PlayIcon />
            Resume
          </DropdownMenuItem>
        )}
        <DropdownMenuSeparator />
        <DropdownMenuItem variant="destructive" onSelect={handleCancel}>
          <XIcon />
          Cancel print
        </DropdownMenuItem>
      </>
    );

  return (
    <div className="flex items-center gap-1">
      {status === 'Printing' && (
        <Button
          type="button"
          size="icon"
          variant="ghost"
          aria-label="Pause"
          onClick={handlePause}
        >
          <PauseIcon data-icon="inline-start" className="" />
        </Button>
      )}
      {status === 'Paused' && (
        <Button
          type="button"
          size="icon"
          variant="ghost"
          aria-label="Resume"
          onClick={handleResume}
        >
          <PlayIcon data-icon="inline-start" className="" />
        </Button>
      )}
      <Button
        type="button"
        size="icon"
        variant="ghost"
        aria-label="Cancel"
        onClick={handleCancel}
      >
        <XIcon data-icon="inline-start" className="" />
      </Button>
    </div>
  );
}
