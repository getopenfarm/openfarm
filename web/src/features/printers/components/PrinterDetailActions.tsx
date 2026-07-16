import { CheckCircle2Icon, SettingsIcon } from 'lucide-react';
import { toast } from 'sonner';
import type { PrinterStatus } from '@/components/printer-tile/status';
import { Button } from '@/components/ui/button';

interface PrinterDetailActionsProps {
  status: PrinterStatus;
  isReachable: boolean;
  isMarkReadyPending: boolean;
  onMarkReady: () => void;
  onOpenSettings: () => void;
}

function notImplemented(action: string) {
  toast(`${action} isn't implemented yet.`);
}

/** Only "Mark ready" and "Settings" (→ EditPrinterDialog) are backed by real mutations today —
 * every other status-driven action here is intentionally decorative, matching the redesign
 * mockup's own lack of wiring for them. Don't fake a state change for the disabled ones. */
export function PrinterDetailActions({
  status,
  isReachable,
  isMarkReadyPending,
  onMarkReady,
  onOpenSettings,
}: PrinterDetailActionsProps) {
  if (!isReachable) {
    return (
      <div className="flex gap-2">
        <Button variant="outline" onClick={() => notImplemented('Reconnect')}>
          Reconnect
        </Button>
        <Button variant="ghost" onClick={onOpenSettings}>
          <SettingsIcon /> Settings
        </Button>
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
    </div>
  );
}
