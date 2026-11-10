import { useQueryClient } from '@tanstack/react-query';
import { PlugIcon, XIcon } from 'lucide-react';
import { useRef, useState } from 'react';
import { Link } from 'react-router';
import { toast } from 'sonner';
import { ConfirmDialog } from '@/components/confirm-dialog/ConfirmDialog';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import {
  type ConnectedAppResponse,
  connectedAppsKey,
  useConnectedApps,
  useRevokeSession,
} from './api';

const day = (iso?: string | null) =>
  iso
    ? new Intl.DateTimeFormat(undefined, {
        dateStyle: 'medium',
        timeZone: 'UTC',
      }).format(new Date(iso))
    : 'Never';

/** Apps signed in through OAuth, as a list with revoke. An app is a session, so revoke ends it. */
export function ConnectedAppList({ userId }: { userId?: string }) {
  const query = useConnectedApps(userId);
  const revoke = useRevokeSession(userId);
  const client = useQueryClient();
  const [target, setTarget] = useState<ConnectedAppResponse | null>(null);
  // Apart from target, so the title holds while the dialog closes.
  const [open, setOpen] = useState(false);
  const [error, setError] = useState('');
  const trigger = useRef<HTMLButtonElement | null>(null);
  const fallback = useRef<HTMLDivElement | null>(null);

  async function confirm() {
    if (!target?.id || revoke.isPending) return;
    setError('');
    try {
      await revoke.mutateAsync(target.id);
      await client.invalidateQueries({ queryKey: connectedAppsKey(userId) });
      setOpen(false);
      toast.success('App access revoked.');
    } catch (e) {
      setError(await extractErrorDetail(e, 'Could not revoke app access.'));
    }
  }

  if (!query.data)
    return query.isError ? (
      <ErrorState
        title="Could not load connected apps"
        onRetry={() => void query.refetch()}
      />
    ) : (
      <Skeleton className="h-32 w-full" />
    );

  const apps = query.data.apps ?? [];
  return (
    <div ref={fallback} tabIndex={-1} className="flex flex-col gap-3">
      {apps.length === 0 ? (
        <EmptyState
          size="sm"
          icon={<PlugIcon className="size-[18px]" />}
          title="No connected apps"
          description={
            userId ? (
              'This user has no connected apps.'
            ) : (
              <>
                An app appears here after you sign in from an AI assistant.
                Assistants that use a token are under{' '}
                <Link to="/profile?tab=tokens" replace>
                  Access tokens
                </Link>
                .
              </>
            )
          }
        />
      ) : (
        <ul className="divide-y rounded-lg border bg-card">
          {apps.map((app) => (
            <li
              key={app.id}
              className="flex flex-wrap items-center justify-between gap-4 p-4"
            >
              <div className="min-w-0 flex-1">
                <div
                  className="truncate text-sm font-semibold"
                  title={app.clientName}
                >
                  {app.clientName}
                </div>
                <p className="mt-0.5 text-xs text-muted-foreground">
                  Returns to {app.returnsTo?.label} · Client ID ofc_…
                  {app.clientIdSuffix}
                </p>
                <div className="mt-2 flex flex-wrap gap-1.5">
                  {app.access?.map((chip) => (
                    <Badge
                      key={chip.area}
                      kind={chip.control ? 'amber' : 'zinc'}
                      title={chip.title}
                      className="cursor-help"
                    >
                      {chip.area}
                      <span className="font-normal opacity-75">
                        {chip.level}
                        {!!chip.extras?.length &&
                          ` + ${chip.extras.join(', ')}`}
                      </span>
                    </Badge>
                  ))}
                </div>
                <dl className="mt-2 flex flex-wrap gap-5 text-xs">
                  {(
                    [
                      ['Connected', app.createdAtUtc],
                      ['Expires (UTC)', app.expiresAtUtc],
                      ['Last used', app.lastUsedAtUtc],
                    ] as const
                  ).map(([label, value]) => (
                    <div key={label}>
                      <dt className="text-muted-foreground">{label}</dt>
                      <dd>{day(value)}</dd>
                    </div>
                  ))}
                </dl>
              </div>
              <Button
                type="button"
                variant="destructive"
                size="sm"
                disabled={revoke.isPending}
                onClick={(e) => {
                  trigger.current = e.currentTarget;
                  setError('');
                  setTarget(app);
                  setOpen(true);
                }}
              >
                <XIcon data-icon="inline-start" /> Revoke access
              </Button>
            </li>
          ))}
        </ul>
      )}
      <ConfirmDialog
        open={open}
        onOpenChange={setOpen}
        title={`Revoke access for “${target?.clientName ?? ''}”?`}
        description="The app is disconnected at once. Connect it again from the assistant to restore access."
        confirmLabel={revoke.isPending ? 'Revoking…' : 'Revoke access'}
        destructive
        isPending={revoke.isPending}
        closeOnConfirm={false}
        error={error || undefined}
        returnFocusRef={trigger}
        fallbackFocusRef={fallback}
        onConfirm={() => void confirm()}
      />
    </div>
  );
}
