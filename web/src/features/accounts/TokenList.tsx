import { useQueryClient } from '@tanstack/react-query';
import { KeyRoundIcon, PlusIcon } from 'lucide-react';
import { useRef, useState } from 'react';
import { Link } from 'react-router';
import { ConfirmDialog } from '@/components/confirm-dialog/ConfirmDialog';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { Alert, AlertDescription } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { type Ability, describeArea } from './describe';
import { TokenCredential } from './TokenCredential';
import {
  type AccessTokenView,
  regenerateToken,
  revokeToken,
  tokenError,
  tokenKey,
  useTokenOptions,
  useTokens,
} from './tokenApi';

const day = (iso?: string | null) =>
  iso
    ? new Intl.DateTimeFormat(undefined, {
        dateStyle: 'medium',
        timeZone: 'UTC',
      }).format(new Date(iso))
    : 'Never';
type Target =
  | { kind: 'regenerate' | 'revoke'; token: AccessTokenView }
  | { kind: 'all' };

export function TokenList({ userId }: { userId?: string }) {
  const query = useTokens(userId);
  const options = useTokenOptions();
  const client = useQueryClient();
  const [target, setTarget] = useState<Target | null>(null);
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [credential, setCredential] = useState<string | null>(null);
  const trigger = useRef<HTMLButtonElement | null>(null);
  const fallback = useRef<HTMLDivElement | null>(null);
  function ask(next: Target, button: HTMLButtonElement) {
    trigger.current = button;
    setError('');
    setTarget(next);
    setOpen(true);
  }
  async function confirm() {
    if (!target || busy) return;
    setBusy(true);
    setError('');
    try {
      setCredential(null);
      if (target.kind === 'regenerate' && target.token.id) {
        const response = await regenerateToken(target.token.id);
        setCredential(response.credential ?? null);
      } else
        await revokeToken(
          target.kind === 'all' ? null : (target.token.id ?? ''),
          userId,
        );
      setOpen(false);
      await client.invalidateQueries({ queryKey: tokenKey(userId) });
    } catch (e) {
      setError(await tokenError(e, 'Could not change this token.'));
    } finally {
      setBusy(false);
    }
  }
  if (!query.data)
    return query.isError ? (
      <ErrorState
        title="Could not load access tokens"
        onRetry={() => void query.refetch()}
      />
    ) : (
      <Skeleton className="h-32 w-full" />
    );
  return (
    <div ref={fallback} tabIndex={-1} className="flex flex-col gap-3">
      {credential && (
        <TokenCredential
          key={credential}
          credential={credential}
          onDone={() => setCredential(null)}
        />
      )}
      {error && !open && (
        <Alert variant="destructive">
          <AlertDescription>{error}</AlertDescription>
        </Alert>
      )}
      {!userId && (
        <Button asChild className="self-start">
          <Link to="/profile/tokens/new">
            <PlusIcon data-icon="inline-start" />
            Add token
          </Link>
        </Button>
      )}
      {query.data.length === 0 ? (
        <EmptyState
          size="sm"
          icon={<KeyRoundIcon className="size-[18px]" />}
          title="No access tokens yet"
          description="A token lets a script or an AI assistant use OpenFarm with only the permissions you choose."
        />
      ) : (
        <ul className="divide-y rounded-lg border bg-card">
          {query.data.map((t) => {
            const expired =
              !!t.expiresAtUtc && Date.parse(t.expiresAtUtc) <= Date.now();
            return (
              <li
                key={t.id}
                className="flex flex-wrap items-center justify-between gap-4 p-4"
              >
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-2 text-sm font-semibold">
                    <span className="break-all">{t.name}</span>
                    <code className="text-xs font-normal text-muted-foreground">
                      ofp_…{t.lastFour}
                    </code>
                    {expired && <Badge kind="amber">Expired</Badge>}
                  </div>
                  <p className="mt-1 text-xs break-words text-muted-foreground">
                    {options.data
                      ? options.data.areas
                          ?.filter((a) =>
                            options.data?.permissions?.some(
                              (p) =>
                                p.area === a.key &&
                                t.permissions?.includes(p.key ?? ''),
                            ),
                          )
                          .map(
                            (a) =>
                              `${a.label}: ${describeArea((options.data?.permissions?.filter((p) => p.area === a.key) ?? []) as Ability[], a.noun ?? '', t.permissions ?? [])}`,
                          )
                          .join(' ') || 'No permissions'
                      : t.permissions?.join(', ') || 'No permissions'}
                  </p>
                  <dl className="mt-2 flex flex-wrap gap-5 text-xs">
                    <div>
                      <dt className="text-muted-foreground">Expires (UTC)</dt>
                      <dd>{day(t.expiresAtUtc)}</dd>
                    </div>
                    <div>
                      <dt className="text-muted-foreground">Last used</dt>
                      <dd>{day(t.lastUsedAtUtc)}</dd>
                    </div>
                  </dl>
                </div>
                <div className="flex gap-2">
                  {!userId && !expired && (
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      disabled={busy}
                      onClick={(e) =>
                        ask({ kind: 'regenerate', token: t }, e.currentTarget)
                      }
                    >
                      Regenerate
                    </Button>
                  )}
                  <Button
                    type="button"
                    variant="destructive"
                    size="sm"
                    disabled={busy}
                    onClick={(e) =>
                      ask({ kind: 'revoke', token: t }, e.currentTarget)
                    }
                  >
                    Revoke
                  </Button>
                </div>
              </li>
            );
          })}
        </ul>
      )}
      {query.data.length > 1 && (
        <Button
          type="button"
          variant="outline"
          className="self-start"
          disabled={busy}
          onClick={(e) => ask({ kind: 'all' }, e.currentTarget)}
        >
          Revoke all {query.data.length} tokens
        </Button>
      )}
      <ConfirmDialog
        open={open}
        onOpenChange={setOpen}
        title={
          target?.kind === 'all'
            ? 'Revoke every token?'
            : `${target?.kind === 'regenerate' ? 'Regenerate' : 'Revoke'} “${target ? target.token.name : ''}”?`
        }
        description={
          target?.kind === 'regenerate'
            ? 'The old credential stops working at once. Update everything that uses it. Permissions and expiry stay the same.'
            : 'Anything using these credentials stops working at once. This cannot be undone.'
        }
        confirmLabel={
          target?.kind === 'regenerate'
            ? 'Regenerate'
            : target?.kind === 'all'
              ? 'Revoke all'
              : 'Revoke'
        }
        destructive
        isPending={busy}
        closeOnConfirm={false}
        onConfirm={() => void confirm()}
        error={error}
        returnFocusRef={trigger}
        fallbackFocusRef={fallback}
      />
    </div>
  );
}
