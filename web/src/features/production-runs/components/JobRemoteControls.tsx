import { PauseIcon, PlayIcon, SquareIcon, XIcon } from 'lucide-react';
import { useEffect, useState } from 'react';
import { toast } from 'sonner';
import { ConfirmDialog } from '@/components/confirm-dialog/ConfirmDialog';
import { PermissionButton as Button } from '@/components/PermissionButton';
import { PermissionMenuItem as DropdownMenuItem } from '@/components/PermissionMenuItem';
import { DropdownMenuSeparator } from '@/components/ui/dropdown-menu';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import {
  useOpenFarmApiEndpointsPrintJobsCancelPrintJobEndpoint,
  useOpenFarmApiEndpointsPrintJobsPausePrintJobEndpoint,
  useOpenFarmApiEndpointsPrintJobsResumePrintJobEndpoint,
} from '@/lib/api/generated/print-jobs/print-jobs';
import { cn } from '@/lib/utils';

type PendingAction = 'pause' | 'resume' | 'cancel' | null;

/** Round tinted control buttons; disabled ones turn grey. Shared with the dashboard's inert placeholders. */
export const ROUND_CONTROL =
  "size-7 rounded-full disabled:bg-muted disabled:text-muted-foreground disabled:opacity-100 [&_svg:not([class*='size-'])]:size-3";
export const ROUND_PAUSE =
  'bg-kind-blue/15 text-kind-blue hover:bg-kind-blue/25 hover:text-kind-blue';
export const ROUND_STOP =
  'bg-kind-red/15 text-kind-red hover:bg-kind-red/25 hover:text-kind-red';

interface JobRemoteControlsProps {
  jobId: string;
  status: string;
  menu?: boolean;
  pending?: PendingAction;
  onPendingChange?: (action: PendingAction) => void;
  /** Dashboard card style: round tinted buttons, and Stop asks first (a misclick there is costly). */
  round?: boolean;
  /** Names the print in the Stop confirmation. */
  name?: string;
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
  round = false,
  name,
}: JobRemoteControlsProps) {
  const [stopOpen, setStopOpen] = useState(false);
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
      toast.error(await extractErrorDetail(error, 'Could not pause job.'));
    }
  }

  async function handleResume() {
    setPendingAction('resume');
    try {
      await resumeMutation.mutateAsync({ id: jobId });
    } catch (error) {
      setPendingAction(null);
      toast.error(await extractErrorDetail(error, 'Could not resume job.'));
    }
  }

  async function handleCancel() {
    setPendingAction('cancel');
    try {
      await cancelMutation.mutateAsync({ id: jobId });
    } catch (error) {
      setPendingAction(null);
      toast.error(await extractErrorDetail(error, 'Could not cancel job.'));
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
          <DropdownMenuItem
            permission="print-jobs:remote-control"
            onSelect={handlePause}
          >
            <PauseIcon />
            Pause
          </DropdownMenuItem>
        ) : (
          <DropdownMenuItem
            permission="print-jobs:remote-control"
            onSelect={handleResume}
          >
            <PlayIcon />
            Resume
          </DropdownMenuItem>
        )}
        <DropdownMenuSeparator />
        <DropdownMenuItem
          permission="print-jobs:cancel"
          variant="destructive"
          onSelect={handleCancel}
        >
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
          permission="print-jobs:remote-control"
          aria-label="Pause"
          className={cn(round && [ROUND_CONTROL, ROUND_PAUSE])}
          onClick={handlePause}
        >
          <PauseIcon
            data-icon="inline-start"
            className={cn(round && 'fill-current')}
          />
        </Button>
      )}
      {status === 'Paused' && (
        <Button
          type="button"
          size="icon"
          variant="ghost"
          permission="print-jobs:remote-control"
          aria-label="Resume"
          className={cn(round && [ROUND_CONTROL, ROUND_PAUSE])}
          onClick={handleResume}
        >
          <PlayIcon
            data-icon="inline-start"
            className={cn(round && 'fill-current')}
          />
        </Button>
      )}
      {round ? (
        <>
          <Button
            type="button"
            size="icon"
            variant="ghost"
            permission="print-jobs:cancel"
            aria-label="Stop print"
            className={cn(ROUND_CONTROL, ROUND_STOP)}
            onClick={() => setStopOpen(true)}
          >
            <SquareIcon data-icon="inline-start" className="fill-current" />
          </Button>
          <ConfirmDialog
            open={stopOpen}
            onOpenChange={setStopOpen}
            title="Stop print?"
            description={`${name ? `${name} stops` : 'The print stops'} now. It cannot be resumed.`}
            confirmLabel="Stop print"
            destructive
            onConfirm={() => {
              setStopOpen(false);
              void handleCancel();
            }}
          />
        </>
      ) : (
        <Button
          type="button"
          size="icon"
          variant="ghost"
          permission="print-jobs:cancel"
          aria-label="Cancel job"
          onClick={handleCancel}
        >
          <XIcon data-icon="inline-start" className="" />
        </Button>
      )}
    </div>
  );
}
