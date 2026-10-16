import {
  type RefObject,
  useCallback,
  useEffect,
  useRef,
  useState,
} from 'react';
import { toast } from 'sonner';
import { ConfirmDialog } from '@/components/confirm-dialog/ConfirmDialog';
import { pluralize } from '@/lib/pluralize';
import {
  PartRemovalDetails,
  type PartRemovalNoticeData,
  type RemovalRow,
} from './PartRemovalToast';
import { checkPartDeletion } from './partRemoval';
import { type RemovablePart, useRemoveParts } from './useRemoveParts';

/** Mounted for one removal attempt. Only a successful preview can open the confirmation. */
export function RemovePartsDialog({
  parts,
  open,
  onOpenChange,
  onRemoved,
  onPendingChange,
  onNotice,
  returnFocusRef,
  fallbackFocusRef,
  bulk = false,
}: {
  parts: RemovablePart[];
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onRemoved?: (ids: string[]) => void;
  onPendingChange?: (pending: boolean) => void;
  onNotice: (notice: PartRemovalNoticeData | undefined) => void;
  returnFocusRef?: RefObject<HTMLElement | null>;
  fallbackFocusRef?: RefObject<HTMLElement | null>;
  bulk?: boolean;
}) {
  const { remove, refresh } = useRemoveParts();
  const [rows, setRows] = useState<RemovalRow[]>([]);
  const [busy, setBusy] = useState(false);
  const running = useRef(false);
  const current = useRef({
    onRemoved,
    onPendingChange,
    onNotice,
    onOpenChange,
    refresh,
  });
  current.current = {
    onRemoved,
    onPendingChange,
    onNotice,
    onOpenChange,
    refresh,
  };
  const targets = useRef(parts.map((part) => ({ ...part })));
  const pendingValue = useRef(false);
  const updatePending = useCallback((pending: boolean) => {
    if (pendingValue.current === pending) return;
    pendingValue.current = pending;
    current.current.onPendingChange?.(pending);
  }, []);
  const focusTrigger = () => {
    const preferred = returnFocusRef?.current;
    const target =
      preferred && !preferred.matches(':disabled')
        ? preferred
        : fallbackFocusRef?.current;
    target?.focus();
  };
  const restoreFocus = useRef(focusTrigger);
  restoreFocus.current = focusTrigger;
  useEffect(() => {
    const request = new AbortController();
    current.current.onNotice({ title: 'Checking parts…', checking: true });
    updatePending(true);
    void (async () => {
      try {
        const results = await checkPartDeletion(
          targets.current.map((p) => p.id),
          request.signal,
        );
        if (request.signal.aborted) return;
        const checked = targets.current.map((part) => ({
          ...part,
          eligibility: results.find((r) => r.partId === part.id),
        }));
        const absent = results
          .filter((r) => r.status === 'Absent')
          .map((r) => r.partId);
        let refreshFailed = false;
        if (absent.length) {
          current.current.onRemoved?.(absent);
          try {
            await current.current.refresh(absent);
          } catch {
            refreshFailed = true;
          }
          if (request.signal.aborted) return;
        }
        updatePending(false);
        if (checked.some((row) => row.eligibility?.status === 'Eligible')) {
          current.current.onNotice(undefined);
          setRows(checked);
        } else {
          current.current.onNotice({
            title: checked.some((row) => row.eligibility?.status === 'Blocked')
              ? bulk
                ? 'Cannot remove parts'
                : 'Cannot remove part'
              : 'Already removed',
            rows: checked,
            message: refreshFailed
              ? 'The list could not be refreshed. Reload the page.'
              : undefined,
          });
          current.current.onOpenChange(false);
          // No modal was opened; keep focus at the initiating control.
          requestAnimationFrame(() => restoreFocus.current());
        }
      } catch {
        if (!request.signal.aborted) {
          updatePending(false);
          current.current.onNotice({
            title: 'Could not remove parts',
            message: 'Could not check part. Try again.',
            rows: targets.current,
          });
          current.current.onOpenChange(false);
          requestAnimationFrame(() => restoreFocus.current());
        }
      }
    })();
    return () => {
      request.abort();
      updatePending(false);
    };
  }, [bulk, updatePending]);

  const eligible = rows.filter((row) => row.eligibility?.status === 'Eligible');
  const retained = rows.filter((row) => row.eligibility?.status !== 'Eligible');
  async function execute() {
    if (running.current || !eligible.length) return;
    running.current = true;
    setBusy(true);
    updatePending(true);
    try {
      const outcomes = await remove(eligible, () => {});
      const cleared = (outcomes ?? [])
        .filter((o) => o.status === 'Removed' || o.status === 'Absent')
        .map((o) => o.part.id);
      const removedCount = (outcomes ?? []).filter(
        (o) => o.status === 'Removed',
      ).length;
      let refreshFailed = false;
      if (cleared.length) {
        current.current.onRemoved?.(cleared);
        try {
          await refresh(cleared);
        } catch {
          refreshFailed = true;
        }
      }
      const remaining: RemovalRow[] = [
        ...retained,
        ...(outcomes ?? [])
          .filter((o) => o.status !== 'Removed')
          .map((outcome) => ({ ...outcome.part, outcome })),
      ];
      if (remaining.length || refreshFailed) {
        current.current.onNotice({
          title: removedCount
            ? `${removedCount} ${removedCount === 1 ? 'part removed' : 'parts removed'}`
            : remaining.every(
                  (row) =>
                    (row.outcome?.status ?? row.eligibility?.status) ===
                    'Absent',
                ) && remaining.length
              ? 'Already removed'
              : 'Parts could not be removed',
          rows: remaining,
          removedCount,
          message: refreshFailed
            ? 'The list could not be refreshed. Reload the page.'
            : undefined,
        });
      } else {
        current.current.onNotice(undefined);
        toast.success(
          bulk
            ? `${pluralize(removedCount, 'part')} removed.`
            : 'Part removed.',
        );
      }
      current.current.onOpenChange(false);
    } finally {
      running.current = false;
      setBusy(false);
      updatePending(false);
    }
  }
  return (
    <ConfirmDialog
      open={open && rows.length > 0}
      onOpenChange={onOpenChange}
      returnFocusRef={returnFocusRef}
      fallbackFocusRef={fallbackFocusRef}
      title={bulk ? 'Remove selected parts?' : 'Remove part?'}
      description={`Permanently remove ${eligible.length === 1 ? eligible[0]?.name : `${eligible.length} parts`}? This cannot be undone.`}
      confirmLabel={
        busy
          ? 'Removing…'
          : `Remove ${eligible.length === 1 ? 'part' : `${eligible.length} parts`}`
      }
      destructive
      isPending={busy}
      confirmDisabled={!eligible.length}
      closeOnConfirm={false}
      onConfirm={() => {
        void execute();
      }}
    >
      <PartRemovalDetails rows={eligible} busy={busy} />
      {retained.length > 0 && (
        <div className="flex flex-col gap-3">
          <p>These parts will not be removed:</p>
          <PartRemovalDetails rows={retained} busy={busy} />
        </div>
      )}
    </ConfirmDialog>
  );
}
