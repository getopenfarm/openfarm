import { CheckCircle2Icon, PencilIcon, Trash2Icon } from 'lucide-react';
import { toast } from 'sonner';
import type { PrinterStatus } from '@/components/printer-tile/status';
import { Button } from '@/components/ui/button';
import { Spinner } from '@/components/ui/spinner';

interface PrinterDetailActionsProps {
  status: PrinterStatus;
  isReachable: boolean;
  isMarkReadyPending: boolean;
  isRemovePending: boolean;
  onMarkReady: () => void;
  onEdit: () => void;
  onRemove: () => void;
}

function notImplemented(action: string) {
  toast(`${action} isn't implemented yet.`);
}

/** Only "Mark ready", "Edit" (→ /printers/:id/edit), and "Remove" are backed by real mutations
 * today — every other status-driven action here is intentionally decorative, matching the redesign
 * mockup's own lack of wiring for them. Don't fake a state change for the disabled ones. Remove is
 * never blocked by status (FR-006) and is offered from every branch below, matching today's
 * fleet-list removal behavior now relocated here (User Story 2). */
export function PrinterDetailActions({
  status,
  isReachable,
  isMarkReadyPending,
  isRemovePending,
  onMarkReady,
  onEdit,
  onRemove,
}: PrinterDetailActionsProps) {
  const removeButton = (
    <Button variant="destructive" onClick={onRemove} disabled={isRemovePending}>
      {isRemovePending ? (
        <Spinner data-icon="inline-start" />
      ) : (
        <Trash2Icon data-icon="inline-start" />
      )}
      {isRemovePending ? 'Removing…' : 'Remove'}
    </Button>
  );

  if (!isReachable) {
    return (
      <div className="flex gap-2">
        <Button variant="outline" onClick={() => notImplemented('Reconnect')}>
          Reconnect
        </Button>
        <Button variant="outline" onClick={onEdit}>
          <PencilIcon data-icon="inline-start" /> Edit
        </Button>
        {removeButton}
      </div>
    );
  }

  if (status === 'AwaitingClear') {
    return (
      <div className="flex gap-2">
        <Button onClick={onMarkReady} disabled={isMarkReadyPending}>
          {isMarkReadyPending ? (
            <Spinner data-icon="inline-start" />
          ) : (
            <CheckCircle2Icon data-icon="inline-start" />
          )}
          {isMarkReadyPending ? 'Marking ready…' : 'Mark ready'}
        </Button>
        <Button variant="ghost" onClick={() => notImplemented('View job')}>
          View job
        </Button>
        {removeButton}
      </div>
    );
  }

  if (status === 'Printing') {
    return (
      <div className="flex gap-2">
        <Button variant="outline" onClick={() => notImplemented('Pause')}>
          Pause
        </Button>
        <Button variant="ghost" onClick={() => notImplemented('Stop')}>
          Stop
        </Button>
        {removeButton}
      </div>
    );
  }

  return (
    <div className="flex gap-2">
      <Button variant="outline" onClick={() => notImplemented('Assign job')}>
        Assign job
      </Button>
      <Button variant="outline" onClick={onEdit}>
        <PencilIcon data-icon="inline-start" /> Edit
      </Button>
      {removeButton}
    </div>
  );
}
