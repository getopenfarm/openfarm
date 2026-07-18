import { CheckCircle2Icon, SettingsIcon, Trash2Icon } from 'lucide-react';
import { toast } from 'sonner';
import type { PrinterStatus } from '@/components/printer-tile/status';
import { Button } from '@/components/ui/button';

interface PrinterDetailActionsProps {
  status: PrinterStatus;
  isReachable: boolean;
  isMarkReadyPending: boolean;
  isRemovePending: boolean;
  onMarkReady: () => void;
  onOpenSettings: () => void;
  onRemove: () => void;
}

function notImplemented(action: string) {
  toast(`${action} isn't implemented yet.`);
}

/** Only "Mark ready", "Settings" (→ EditPrinterDialog), and "Remove" are backed by real mutations
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
  onOpenSettings,
  onRemove,
}: PrinterDetailActionsProps) {
  const removeButton = (
    <Button
      variant="ghost"
      onClick={onRemove}
      disabled={isRemovePending}
      className="text-destructive hover:text-destructive"
    >
      <Trash2Icon />
      {isRemovePending ? 'Removing…' : 'Remove'}
    </Button>
  );

  if (!isReachable) {
    return (
      <div className="flex gap-2">
        <Button variant="outline" onClick={() => notImplemented('Reconnect')}>
          Reconnect
        </Button>
        <Button variant="ghost" onClick={onOpenSettings}>
          <SettingsIcon /> Settings
        </Button>
        {removeButton}
      </div>
    );
  }

  if (status === 'AwaitingClear') {
    return (
      <div className="flex gap-2">
        <Button onClick={onMarkReady} disabled={isMarkReadyPending}>
          <CheckCircle2Icon />
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
      <Button variant="ghost" onClick={onOpenSettings}>
        <SettingsIcon /> Settings
      </Button>
      {removeButton}
    </div>
  );
}
