import { useQueryClient } from '@tanstack/react-query';
import { type RefObject, useId, useState } from 'react';
import { toast } from 'sonner';
import { ConfirmDialog } from '@/components/confirm-dialog/ConfirmDialog';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import { openFarmApiEndpointsSpoolsUnloadSpoolEndpoint } from '@/lib/api/generated/spools/spools';
import { formatGrams } from '@/lib/formatUnits';
import { invalidateFilamentTrackingQueries } from '@/lib/invalidateFilamentTrackingQueries';

export interface FinishTarget {
  id: string;
  name: string;
  remainingGrams: number;
}

const QUICK_REASONS = ['Used up', 'Damaged', 'Wet'];

interface FinishSpoolsDialogProps {
  /** Open while non-empty. */
  targets: FinishTarget[];
  onOpenChange: (open: boolean) => void;
  /** The spools that finished, before the dialog closes. */
  onFinished?: (ids: string[]) => void;
  returnFocusRef?: RefObject<HTMLElement | null>;
}

/** Finishes spools one after another, writing off what is left on each; a failure skips only that spool. */
export function FinishSpoolsDialog({
  targets,
  onOpenChange,
  onFinished,
  returnFocusRef,
}: FinishSpoolsDialogProps) {
  const queryClient = useQueryClient();
  const reasonId = useId();
  const [reason, setReason] = useState('');
  const [pending, setPending] = useState(false);
  const writeOff = targets.reduce((sum, t) => sum + t.remainingGrams, 0);

  async function finish() {
    setPending(true);
    const finished: string[] = [];
    try {
      for (const target of targets) {
        try {
          await openFarmApiEndpointsSpoolsUnloadSpoolEndpoint(target.id, {
            action: 'MarkFinished',
            reason: reason.trim() || null,
          });
          finished.push(target.id);
        } catch (error) {
          toast.error(`Could not finish ${target.name}.`, {
            description: await extractErrorDetail(error, 'Request failed.'),
          });
        }
      }
      if (finished.length) {
        toast.success(
          finished.length === 1
            ? 'Spool finished.'
            : `${finished.length} spools finished.`,
        );
        onFinished?.(finished);
      }
      invalidateFilamentTrackingQueries(queryClient);
    } finally {
      setPending(false);
      setReason('');
      onOpenChange(false);
    }
  }

  return (
    <ConfirmDialog
      open={targets.length > 0}
      onOpenChange={(open) => {
        if (!open) setReason('');
        onOpenChange(open);
      }}
      returnFocusRef={returnFocusRef}
      title={
        targets.length === 1
          ? `Finish ${targets[0].name}?`
          : `Finish ${targets.length} spools?`
      }
      description={`Writes off ${formatGrams(writeOff)}. Restore puts it back.`}
      confirmLabel={pending ? 'Finishing…' : 'Finish'}
      isPending={pending}
      closeOnConfirm={false}
      onConfirm={() => {
        void finish();
      }}
    >
      <div className="flex flex-col gap-2">
        <Label htmlFor={reasonId}>Reason (optional)</Label>
        <ToggleGroup
          type="single"
          variant="outline"
          size="sm"
          aria-label="Common reasons"
          className="justify-start"
          disabled={pending}
          value={QUICK_REASONS.includes(reason) ? reason : ''}
          onValueChange={setReason}
        >
          {QUICK_REASONS.map((quick) => (
            <ToggleGroupItem key={quick} value={quick}>
              {quick}
            </ToggleGroupItem>
          ))}
        </ToggleGroup>
        <Input
          id={reasonId}
          value={reason}
          maxLength={200}
          disabled={pending}
          placeholder="e.g. tangled"
          onChange={(event) => setReason(event.target.value)}
        />
      </div>
    </ConfirmDialog>
  );
}
