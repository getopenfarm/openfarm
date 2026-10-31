import { useQueryClient } from '@tanstack/react-query';
import { MonitorIcon, XIcon } from 'lucide-react';
import { useState } from 'react';
import { useNavigate } from 'react-router';
import { toast } from 'sonner';
import { ConfirmDialog } from '@/components/confirm-dialog/ConfirmDialog';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { Button } from '@/components/ui/button';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import { useIdentity } from '@/lib/auth/permissions';
import { forgetSession } from '@/lib/auth/session';
import {
  type SessionResponse,
  useRevokeAllSessions,
  useRevokeSession,
  useSessions,
} from './api';

const dateFormat = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' });
const day = (iso?: string | null) =>
  iso ? dateFormat.format(new Date(iso)) : '—';

interface SessionListProps {
  /** Another user's sessions (users:manage). Omit for the caller's own. */
  userId?: string;
  emptyText?: string;
}

/** Browser sessions as a list, with revoke one and revoke all. */
export function SessionList({
  userId,
  emptyText = 'No active sessions.',
}: SessionListProps) {
  const identity = useIdentity();
  const client = useQueryClient();
  const navigate = useNavigate();
  const sessions = useSessions(userId);
  const revokeOne = useRevokeSession(userId);
  const revokeAll = useRevokeAllSessions(userId);
  const [confirming, setConfirming] = useState<SessionResponse | 'all' | null>(
    null,
  );
  // Apart from confirming, so the title holds while the dialog closes.
  const [open, setOpen] = useState(false);
  const [error, setError] = useState('');
  const own = !userId || userId === identity.data?.userId;
  const busy = revokeOne.isPending || revokeAll.isPending;

  async function confirm() {
    const target = confirming;
    setOpen(false);
    setError('');
    try {
      if (target === 'all') await revokeAll.mutateAsync();
      else if (target?.id) await revokeOne.mutateAsync(target.id);
      // Revoking this browser's session signs it out. The admin revoke-all keeps it.
      if (own && (target === 'all' ? !userId : target?.isCurrent)) {
        forgetSession();
        client.clear();
        navigate('/login');
        return;
      }
      toast.success(target === 'all' ? 'Sessions revoked' : 'Session revoked');
    } catch (e) {
      setError(await extractErrorDetail(e, 'Could not revoke session.'));
    }
  }

  if (!sessions.data)
    return sessions.isError ? (
      <ErrorState
        title="Could not load sessions"
        onRetry={() => void sessions.refetch()}
      />
    ) : (
      <span className="text-[12.5px] text-muted-foreground">
        Loading sessions…
      </span>
    );

  const list = sessions.data;
  return (
    <div className="flex flex-col gap-3">
      {error && (
        <p role="alert" className="text-[13px] text-destructive">
          {error}
        </p>
      )}
      {list.length === 0 ? (
        <EmptyState
          size="sm"
          icon={<MonitorIcon className="size-[18px]" />}
          title="No active sessions"
          description={emptyText}
        />
      ) : (
        <ul className="rounded-lg border bg-card">
          {list.map((s) => (
            <li
              key={s.id}
              className="grid grid-cols-1 items-center gap-2.5 border-t px-4 py-3.5 first:border-t-0 md:grid-cols-[minmax(0,1fr)_auto_auto] md:gap-5"
            >
              <div className="flex min-w-0 items-baseline gap-2 text-[13.5px] font-semibold">
                <span className="truncate" title={s.device}>
                  {s.device || 'Browser'}
                </span>
                {s.isCurrent && own && (
                  <span className="shrink-0 text-[11.5px] font-normal text-muted-foreground">
                    This device
                  </span>
                )}
              </div>
              <dl className="m-0 flex gap-5">
                {(
                  [
                    ['Signed in', s.createdAtUtc],
                    ['Last used', s.lastSeenAtUtc],
                    ['Expires', s.expiresAtUtc],
                  ] as const
                ).map(([label, value]) => (
                  <div key={label} className="min-w-[92px]">
                    <dt className="text-[11.5px] text-muted-foreground">
                      {label}
                    </dt>
                    <dd className="mt-0.5 text-[12.5px] whitespace-nowrap">
                      {day(value)}
                    </dd>
                  </div>
                ))}
              </dl>
              <div className="-ml-2.5 flex md:ml-0">
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  className="text-destructive hover:bg-destructive/10 hover:text-destructive"
                  disabled={busy}
                  onClick={() => {
                    setConfirming(s);
                    setOpen(true);
                  }}
                >
                  <XIcon data-icon="inline-start" /> Revoke
                </Button>
              </div>
            </li>
          ))}
        </ul>
      )}
      {list.length > 1 && (
        <button
          type="button"
          className="self-start text-[12.5px] text-muted-foreground underline underline-offset-3 hover:text-destructive"
          disabled={busy}
          onClick={() => {
            setConfirming('all');
            setOpen(true);
          }}
        >
          Revoke all {list.length} sessions
        </button>
      )}
      <ConfirmDialog
        open={open}
        onOpenChange={setOpen}
        title={
          confirming === 'all'
            ? 'Revoke every session?'
            : `Revoke "${confirming?.device || 'Browser'}"?`
        }
        description={
          confirming !== 'all'
            ? 'That browser is signed out at once. This cannot be undone.'
            : !userId
              ? 'Every browser signed in as you, this one included, is signed out at once. This cannot be undone.'
              : 'Every browser signed in as this user is signed out at once. This cannot be undone.'
        }
        confirmLabel={confirming === 'all' ? 'Revoke all' : 'Revoke'}
        destructive
        onConfirm={() => void confirm()}
      />
    </div>
  );
}
