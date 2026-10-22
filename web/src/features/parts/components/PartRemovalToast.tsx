import { useEffect } from 'react';
import { toast } from 'sonner';
import { pluralize } from '@/lib/pluralize';
import type { DeletionEligibility } from './partRemoval';
import type { PartRemovalOutcome, RemovablePart } from './useRemoveParts';

export type RemovalRow = RemovablePart & {
  eligibility?: DeletionEligibility;
  outcome?: PartRemovalOutcome;
};
export interface PartRemovalNoticeData {
  title: string;
  rows?: RemovalRow[];
  message?: string;
  checking?: boolean;
  removedCount?: number;
}

export function PartRemovalDetails({
  rows,
  busy = false,
  showNames = true,
}: {
  rows: RemovalRow[];
  busy?: boolean;
  showNames?: boolean;
}) {
  return (
    <ul className="flex flex-col gap-3">
      {rows.map((row) => {
        const status = row.outcome?.status ?? row.eligibility?.status;
        const blockers = row.outcome?.blockers?.length
          ? row.outcome.blockers
          : (row.eligibility?.blockers ?? []);
        return (
          <li key={row.id} className="min-w-0 break-words">
            {showNames && <strong>{row.name}</strong>}
            {blockers.map((blocker) => (
              <div key={`${blocker.code}-${blocker.entityId}`}>
                {blocker.code === 'AssemblyReference'
                  ? 'Used by assembly: '
                  : blocker.code === 'UnlimitedRun'
                    ? 'Used by unlimited run: '
                    : 'Needed by unfinished job: '}
                <a
                  className="underline"
                  aria-disabled={busy || undefined}
                  tabIndex={busy ? -1 : undefined}
                  onClick={(event) => {
                    if (busy) event.preventDefault();
                  }}
                  href={`/${blocker.entityType === 'Assembly' ? 'assemblies' : blocker.entityType === 'ProductionRun' ? 'production-runs' : 'jobs'}/${blocker.entityId}`}
                >
                  {blocker.name}
                </a>
              </div>
            ))}
            {status === 'Blocked' && !blockers.length && (
              <div>{row.outcome?.reason ?? 'This part is still in use.'}</div>
            )}
            {status === 'Absent' && (
              <div>Already removed from the library.</div>
            )}
            {status === 'Recheck' && (
              <div>This part changed. Try removing it again.</div>
            )}
            {status === 'Failed' && (
              <div>
                {row.outcome?.httpStatus === 401 ||
                row.outcome?.httpStatus === 403
                  ? 'You do not have permission to remove this part.'
                  : 'Removal could not be confirmed. Try again.'}
              </div>
            )}
            {status === 'NotAttempted' && (
              <div>Not removed because access was denied.</div>
            )}
            {status === 'Eligible' &&
              !!row.eligibility?.legacyJobsLosingRecovery && (
                <div>
                  {pluralize(
                    row.eligibility.legacyJobsLosingRecovery,
                    'finished job',
                  )}{' '}
                  will permanently lose retry and download.
                </div>
              )}
          </li>
        );
      })}
    </ul>
  );
}

/** Bridges removal feedback into the application's existing Sonner toaster. */
export function PartRemovalToast({
  notice,
}: {
  notice?: PartRemovalNoticeData;
}) {
  useEffect(() => {
    if (!notice) return;
    // Sonner's Toaster re-subscribes in an effect of its own. When both re-run in one commit, every cleanup runs before any
    // setup, so a toast created here would reach no subscriber and never show. Creating it after the commit avoids that.
    let notificationIds: Array<string | number> = [];
    let cancelled = false;
    queueMicrotask(() => {
      if (!cancelled) notificationIds = show(notice);
    });
    return () => {
      cancelled = true;
      for (const notificationId of notificationIds)
        toast.dismiss(notificationId);
    };
  }, [notice]);
  return null;
}

/** Shows a notice as toasts and returns their ids. */
function show(notice: PartRemovalNoticeData): Array<string | number> {
  if (notice.checking) {
    return [
      toast.loading(notice.title, {
        duration: Number.POSITIVE_INFINITY,
        dismissible: false,
      }),
    ];
  }
  const persistent = {
    duration: Number.POSITIVE_INFINITY,
    closeButton: true,
    dismissible: true,
  };
  const notificationIds: Array<string | number> = [];
  if (notice.removedCount) {
    const notify = notice.message ? toast.warning : toast.success;
    notificationIds.push(
      notify(
        `${notice.removedCount} ${notice.removedCount === 1 ? 'part removed' : 'parts removed'}`,
        {
          description: notice.message,
          closeButton: true,
          duration: notice.message ? Number.POSITIVE_INFINITY : 5000,
        },
      ),
    );
  }
  for (const row of notice.rows ?? []) {
    const status = row.outcome?.status ?? row.eligibility?.status;
    const notify =
      status === 'Blocked' || status === 'Recheck'
        ? toast.warning
        : status === 'Absent'
          ? toast.info
          : toast.error;
    notificationIds.push(
      notify(
        `${status === 'Absent' ? 'Already removed' : 'Cannot remove'} ${row.name}`,
        {
          ...persistent,
          description: (
            <div className="flex max-h-64 flex-col gap-3 overflow-y-auto">
              {!notice.removedCount && notice.message && (
                <p>{notice.message}</p>
              )}
              <PartRemovalDetails rows={[row]} showNames={false} />
            </div>
          ),
        },
      ),
    );
  }
  if (!notice.rows?.length && !notice.removedCount) {
    notificationIds.push(
      toast.warning(notice.title, {
        ...persistent,
        description: notice.message,
      }),
    );
  }
  return notificationIds;
}
