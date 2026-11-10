import { useQuery } from '@tanstack/react-query';
import { TriangleAlertIcon } from 'lucide-react';
import { type FormEvent, useState } from 'react';
import { useLocation, useNavigate, useSearchParams } from 'react-router';
import { toast } from 'sonner';
import { ErrorState } from '@/components/empty-state/EmptyState';
import { Alert, AlertDescription } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import {
  Field,
  FieldContent,
  FieldDescription,
  FieldError,
  FieldLabel,
  FieldLegend,
  FieldSet,
} from '@/components/ui/field';
import { Skeleton } from '@/components/ui/skeleton';
import { Spinner } from '@/components/ui/spinner';
import type {
  ConsentDecisionResponse,
  ConsentResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { httpClient } from '@/lib/api/httpClient';
import { useAuth } from '@/lib/auth/useAuth';
import {
  ConnectFrame,
  ConnectMessage,
  useConnectTitle,
} from './ConnectErrorPage';

const consentUrl = '/api/v1/auth/oauth/consent';
const NOTHING_SELECTED = 'Select at least one available permission.';
const EXPIRED =
  'Connection request expired. Start again from your AI assistant.';
const FAILED = 'Could not connect the app. Start again from your AI assistant.';

const TIERS: Record<string, { heading: string; note?: string; red?: boolean }> =
  {
    Read: { heading: 'Read' },
    Change: {
      heading: 'Change',
      note: 'The app can change the queue and record maintenance, as you could yourself. Changes are saved straight away and appear in the activity log under your name. Requeued jobs start on idle printers on their own.',
    },
    PrinterControl: {
      heading: 'Printer control',
      note: 'The app can pause and stop running prints. Each command reaches the printer at once, and a stopped print cannot be resumed. Allow this only for an app you trust with your printers.',
      red: true,
    },
  };

const day = (iso?: string) =>
  iso
    ? new Intl.DateTimeFormat(undefined, {
        dateStyle: 'medium',
        timeZone: 'UTC',
      }).format(new Date(iso))
    : '';

async function problemCode(error: unknown) {
  if (!(error instanceof Response)) return undefined;
  try {
    return (await error.clone().json())?.code as string | undefined;
  } catch {
    return undefined;
  }
}

/** The consent screen an AI assistant's sign-in lands on (akaun's `/oauth/authorize`). */
export function ConsentPage() {
  const [params] = useSearchParams();
  const request = params.get('request') ?? '';
  useConnectTitle();
  const query = useQuery({
    queryKey: [consentUrl, request],
    enabled: !!request,
    refetchOnWindowFocus: false,
    // A 400 is an expired, used or tampered request; a 404 means sign-in is off.
    queryFn: async (): Promise<ConsentResponse | 'expired' | 'disabled'> => {
      try {
        return (
          await httpClient<{ data: ConsentResponse }>(
            `${consentUrl}?request=${encodeURIComponent(request)}`,
          )
        ).data;
      } catch (e) {
        if (e instanceof Response && e.status === 400) return 'expired';
        if (e instanceof Response && e.status === 404) return 'disabled';
        throw e;
      }
    },
  });
  const consent = request ? query.data : 'expired';
  return (
    <ConnectFrame>
      {consent === 'expired' || consent === 'disabled' ? (
        <ConnectMessage code={consent === 'disabled' ? 'disabled' : null} />
      ) : consent ? (
        <ConsentForm request={request} consent={consent} />
      ) : query.isError ? (
        <ErrorState
          title="Could not load the connection request"
          onRetry={() => void query.refetch()}
        />
      ) : (
        <div className="flex flex-col gap-3">
          <Skeleton className="h-8 w-2/3" />
          <Skeleton className="h-4 w-full" />
          <Skeleton className="h-4 w-5/6" />
          <Skeleton className="h-40 w-full" />
        </div>
      )}
    </ConnectFrame>
  );
}

function ConsentForm({
  request,
  consent,
}: {
  request: string;
  consent: ConsentResponse;
}) {
  const auth = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const groups = (consent.tiers ?? []).filter(
    (t) => t.tier && TIERS[t.tier] && t.scopes?.length,
  );
  const scopes = groups.flatMap((g) =>
    (g.scopes ?? []).map((s) => ({ ...s, tier: g.tier })),
  );
  // Read is pre-ticked; Change and Printer control never are.
  const [selected, setSelected] = useState(
    () =>
      new Set(
        scopes
          .filter((s) => s.allowed && s.preselected && s.tier === 'Read')
          .map((s) => s.scope ?? ''),
      ),
  );
  const [pending, setPending] = useState<'approve' | 'deny' | null>(null);
  const [returning, setReturning] = useState(false);
  const [scopeError, setScopeError] = useState(false);
  const [error, setError] = useState('');
  const name = consent.clientName ?? 'app';
  const label = consent.returnsTo?.label ?? '';
  const busy = pending !== null || returning;

  async function decide(approve: boolean) {
    if (busy) return;
    const chosen = scopes
      .filter((s) => s.allowed && selected.has(s.scope ?? ''))
      .map((s) => s.scope ?? '');
    setError('');
    setScopeError(false);
    if (approve && chosen.length === 0) {
      setScopeError(true);
      return;
    }
    setPending(approve ? 'approve' : 'deny');
    try {
      const response = await httpClient<{ data: ConsentDecisionResponse }>(
        consentUrl,
        {
          method: 'POST',
          body: JSON.stringify({ request, approve, scopes: chosen }),
        },
      );
      const target = response.data.redirectUri;
      if (!target) throw new Error('No redirect');
      setReturning(true);
      window.location.assign(target);
    } catch (e) {
      const code = await problemCode(e);
      if (code === 'invalid_scope') setScopeError(true);
      else
        setError(e instanceof Response && e.status === 400 ? EXPIRED : FAILED);
    } finally {
      setPending(null);
    }
  }

  async function signOut() {
    try {
      await auth.logout();
      navigate(
        `/login?returnTo=${encodeURIComponent(location.pathname + location.search)}`,
        { replace: true },
      );
    } catch {
      toast.error('Could not sign out.');
    }
  }

  return (
    <form
      className="flex flex-col gap-6"
      onSubmit={(e: FormEvent) => {
        e.preventDefault();
        void decide(true);
      }}
    >
      <div className="flex flex-col gap-3">
        <h1 className="text-2xl font-semibold break-words">Connect {name}</h1>
        <p>
          Signed in as <strong>{consent.username}</strong> on {consent.instance}
          . Not {consent.username}?{' '}
          <Button
            type="button"
            variant="link"
            className="h-auto p-0 align-baseline text-base"
            disabled={busy}
            onClick={() => void signOut()}
          >
            Sign out
          </Button>
        </p>
        <p>
          Connect this app to your farm. Choose the permissions you want to
          allow. It can never do more than your OpenFarm account can.
        </p>
        <div className="flex flex-col gap-1 text-sm text-muted-foreground">
          <p>
            The app name is supplied by the client. Client ID:{' '}
            <code className="break-all">{consent.clientId}</code>
          </p>
          <p>Returns to {label}.</p>
        </div>
        {consent.unverifiedName && (
          <p className="flex items-start gap-2 text-sm text-kind-amber">
            <TriangleAlertIcon className="mt-0.5 size-4 shrink-0" />
            The app name is not verified. Allow only if you started this from{' '}
            {label}.
          </p>
        )}
      </div>

      <fieldset disabled={busy} className="contents">
        {groups.map((group) => {
          const tier = TIERS[group.tier ?? ''];
          return (
            <FieldSet key={group.tier} className="gap-3">
              <FieldLegend className="mb-0">{tier.heading}</FieldLegend>
              {tier.note && (
                <FieldDescription
                  className={tier.red ? 'text-destructive' : undefined}
                >
                  {tier.note}
                </FieldDescription>
              )}
              {group.scopes?.map((s) => {
                const id = `scope-${s.scope}`;
                return (
                  <Field
                    key={s.scope}
                    orientation="horizontal"
                    data-disabled={!s.allowed || undefined}
                  >
                    <Checkbox
                      id={id}
                      disabled={!s.allowed}
                      checked={!!s.allowed && selected.has(s.scope ?? '')}
                      onCheckedChange={(checked) => {
                        setScopeError(false);
                        setSelected((current) => {
                          const next = new Set(current);
                          if (checked) next.add(s.scope ?? '');
                          else next.delete(s.scope ?? '');
                          return next;
                        });
                      }}
                    />
                    <FieldContent>
                      <FieldLabel htmlFor={id}>{s.label ?? s.scope}</FieldLabel>
                      {!!s.includes?.length && (
                        <FieldDescription className="text-xs">
                          Includes: {s.includes.join(', ')}
                        </FieldDescription>
                      )}
                      {!s.allowed && (
                        <FieldDescription>
                          Your account does not have this permission.
                        </FieldDescription>
                      )}
                    </FieldContent>
                  </Field>
                );
              })}
            </FieldSet>
          );
        })}
      </fieldset>

      <div className="flex flex-col gap-3">
        <p className="text-sm text-muted-foreground">
          Access ends {day(consent.accessEndsAtUtc)} (UTC). Revoke it any time
          in My profile › Connected apps.
        </p>
        {scopeError && <FieldError>{NOTHING_SELECTED}</FieldError>}
        {error && (
          <Alert variant="destructive">
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        )}
        <div className="flex flex-wrap gap-3">
          <Button
            type="submit"
            disabled={busy || !scopes.some((s) => s.allowed)}
          >
            {pending === 'approve' && <Spinner data-icon="inline-start" />}
            {pending === 'approve' ? 'Allowing…' : 'Allow selected access'}
          </Button>
          <Button
            type="button"
            variant="outline"
            disabled={busy}
            onClick={() => void decide(false)}
          >
            {pending === 'deny' && <Spinner data-icon="inline-start" />}
            {pending === 'deny' ? 'Canceling…' : 'Cancel'}
          </Button>
        </div>
        {returning ? (
          <p role="status" className="text-sm text-muted-foreground">
            Returning to {label}…
          </p>
        ) : (
          <p className="text-sm text-muted-foreground">
            If you did not start this connection from your assistant just now,
            choose Cancel.
          </p>
        )}
      </div>
    </form>
  );
}
