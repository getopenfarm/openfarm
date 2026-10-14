import { useCallback, useEffect, useRef } from 'react';
import { useBlocker } from 'react-router';
import { ConfirmDialog } from '@/components/confirm-dialog/ConfirmDialog';

export function useUnsavedChangesPrompt(isDirty: boolean) {
  // useBlocker memoizes its internal check off *this* callback's identity, not off the value it
  // reads — reading a ref here (kept fresh every render) means an imperative markClean() call
  // takes effect on the very next navigation attempt, with no render round-trip in between. That
  // gap matters: a mutation's onSuccess calling navigate() right after a successful save would
  // otherwise still observe the stale pre-save `isDirty=true` and wrongly block its own navigate.
  const isDirtyRef = useRef(isDirty);
  isDirtyRef.current = isDirty;

  const shouldBlock = useCallback(() => isDirtyRef.current, []);
  const blocker = useBlocker(shouldBlock);

  useEffect(() => {
    if (!isDirty) return;

    function handleBeforeUnload(event: BeforeUnloadEvent) {
      event.preventDefault();
    }

    window.addEventListener('beforeunload', handleBeforeUnload);
    return () => window.removeEventListener('beforeunload', handleBeforeUnload);
  }, [isDirty]);

  function ConfirmLeaveDialog() {
    if (blocker.state !== 'blocked') return null;

    return (
      <ConfirmDialog
        open
        onOpenChange={(open) => {
          if (!open) blocker.reset();
        }}
        title="Discard unsaved changes?"
        description="You have unsaved changes. Leaving this page will discard them."
        confirmLabel="Discard changes"
        destructive
        onConfirm={() => blocker.proceed()}
      />
    );
  }

  // Call this immediately before an intentional programmatic navigate() right after a
  // successful save, so that navigation isn't blocked by the form's now-stale dirty state.
  function markClean() {
    isDirtyRef.current = false;
  }

  return { ConfirmLeaveDialog, markClean };
}
