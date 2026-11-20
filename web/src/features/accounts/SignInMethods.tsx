import { useQuery } from '@tanstack/react-query';
import { KeyRound, LogIn } from 'lucide-react';
import { type FormEvent, type ReactNode, useEffect, useState } from 'react';
import { useSearchParams } from 'react-router';
import { toast } from 'sonner';
import { ErrorState } from '@/components/empty-state/EmptyState';
import { Alert, AlertDescription } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Field, FieldError, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { Skeleton } from '@/components/ui/skeleton';
import { Spinner } from '@/components/ui/spinner';
import {
  SettingsRows,
  SettingsSection,
} from '@/features/settings/SettingsParts';
import { apiUrl } from '@/lib/api/apiUrl';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import type { BeginOidcLinkResponse } from '@/lib/api/generated/endpoints.schemas';
import { httpClient } from '@/lib/api/httpClient';

interface Identity {
  enabled: boolean;
  displayName: string;
  linked: boolean;
  passwordAllowed: boolean;
}

const linkFailureMessages: Record<string, string> = {
  provider_configuration:
    'Provider configuration load failed. Check the issuer URL and the connection to the provider. Administrators: see the server log.',
  provider_response:
    'The provider response failed verification. Ask an administrator to check the issuer, the client settings, the signing key, and the server log.',
  provider_denied:
    'The provider refused the sign-in. Make sure your provider account can use OpenFarm. Then start linking again.',
  issuer_mismatch:
    'The provider issuer does not match the saved issuer URL. Ask an administrator to enter the exact issuer, with any trailing slash.',
  auth_time_missing:
    'The provider did not send auth_time. Trying again does not help. Ask an administrator to add auth_time to the ID token.',
  fresh_authentication:
    'The provider did not confirm a recent sign-in. Sign out of the provider. Then start linking again. If this continues, ask an administrator to check the provider flow and the server clocks.',
  browser_proof:
    'The browser proof for linking is missing or does not match. Start linking again in the same browser. Use the same OpenFarm hostname.',
  session_changed:
    'Your OpenFarm session changed or ended. Sign in to your local account again. Then start linking from this browser.',
  expired: 'The linking request expired. Start a new request in My profile.',
  request_replaced:
    'The linking request was used or replaced. Start a new request. Complete only that request.',
  configuration_changed:
    'The sign-in settings changed. Start a new linking request.',
  already_linked:
    'This account is already linked to the provider. Reload My profile to see its status.',
  identity_conflict:
    'Another OpenFarm account already uses this provider identity. Contact an administrator.',
};

/**
 * Starts a link. The server skips the password right after a password sign-in
 * (`BrowserSessionService.RecentSignInWindow`); otherwise it answers
 * `passwordRequired` and the caller asks for it.
 */
async function beginLink(
  password?: string,
): Promise<'redirected' | 'password'> {
  // Issue antiforgery proof for the same authenticated user as the linking POST.
  const csrf = await httpClient<{ data: { token: string } }>(
    '/api/v1/auth/csrf',
    { cache: 'no-store' },
  );
  const response = await httpClient<{ data: BeginOidcLinkResponse }>(
    '/api/v1/auth/sso/link',
    {
      method: 'POST',
      headers: { 'X-CSRF-TOKEN': csrf.data.token },
      body: JSON.stringify(password ? { password } : {}),
    },
  );
  const { redirectUri, passwordRequired } = response.data;
  if (passwordRequired || !redirectUri) return 'password';
  window.location.assign(apiUrl(redirectUri));
  return 'redirected';
}

/** One way to sign in: what it is, its state, and its one action. */
function MethodRow({
  icon,
  title,
  status,
  action,
}: {
  icon: ReactNode;
  title: ReactNode;
  status: ReactNode;
  action?: ReactNode;
}) {
  return (
    <div className="flex items-center gap-3 py-4">
      <span
        aria-hidden
        className="flex size-8 shrink-0 items-center justify-center rounded-md bg-muted text-muted-foreground [&>svg]:size-4"
      >
        {icon}
      </span>
      <div className="min-w-0 flex-1">
        <p className="text-sm font-medium">{title}</p>
        <p className="text-sm text-muted-foreground">{status}</p>
      </div>
      {action}
    </div>
  );
}

/**
 * My profile › Security: a row per sign-in method. Change password opens
 * `passwordForm` below the list; `close` hides it again.
 */
export function SignInMethods({
  passwordForm,
}: {
  passwordForm: (close: () => void) => ReactNode;
}) {
  const [params, setParams] = useSearchParams();
  const [editingPassword, setEditingPassword] = useState(false);
  const [linking, setLinking] = useState(false);
  const [linkError, setLinkError] = useState('');
  const [confirming, setConfirming] = useState(false);
  const identity = useQuery({
    queryKey: ['auth', 'sso', 'identity'],
    queryFn: async () =>
      (await httpClient<{ data: Identity }>('/api/v1/auth/sso/identity')).data,
    retry: false,
  });
  const sso = identity.data?.enabled ? identity.data : undefined;
  const passwordAllowed = identity.data?.passwordAllowed !== false;

  // The Linked badge shows the result; announce it once and drop the query.
  const justLinked = params.get('sso') === 'linked';
  useEffect(() => {
    if (!justLinked || !sso) return;
    toast.success(`${sso.displayName} linked.`, { id: 'sso-linked' });
    setParams({ tab: 'security' }, { replace: true });
  }, [justLinked, sso, setParams]);

  async function link() {
    if (linking) return;
    setLinking(true);
    setLinkError('');
    try {
      if ((await beginLink()) === 'redirected') return;
      setConfirming(true);
    } catch (e) {
      setLinkError(await extractErrorDetail(e, 'Link failed. Try again.'));
    }
    setLinking(false);
  }

  if (identity.isPending) return <Skeleton className="h-36 max-w-[640px]" />;

  const failure =
    params.get('sso') === 'failed'
      ? ((Object.hasOwn(linkFailureMessages, params.get('ssoReason') ?? '')
          ? linkFailureMessages[params.get('ssoReason') ?? '']
          : undefined) ??
        'Link failed. Start again, or contact an administrator.')
      : linkError;

  return (
    <div className="flex flex-col gap-8">
      <SettingsSection
        title="Sign-in methods"
        description="How you sign in to OpenFarm."
      >
        {identity.isError && (
          <ErrorState
            title="Load failed"
            onRetry={() => void identity.refetch()}
          />
        )}
        {failure && (
          <Alert variant="destructive" className="mb-4">
            <AlertDescription>{failure}</AlertDescription>
          </Alert>
        )}
        <SettingsRows>
          <MethodRow
            icon={<KeyRound />}
            title="Password"
            status={
              passwordAllowed
                ? 'Username and password'
                : `Turned off. Sign in with ${sso?.displayName ?? 'SSO'}.`
            }
            action={
              passwordAllowed && (
                <Button
                  variant="outline"
                  size="sm"
                  aria-expanded={editingPassword}
                  disabled={editingPassword}
                  onClick={() => setEditingPassword(true)}
                >
                  Change
                </Button>
              )
            }
          />
          {sso && (
            <MethodRow
              icon={<LogIn />}
              title={sso.displayName}
              status={
                sso.linked
                  ? `Sign in with ${sso.displayName}`
                  : 'Not linked. Linking keeps your permissions.'
              }
              action={
                sso.linked ? (
                  <Badge kind="green">Linked</Badge>
                ) : (
                  <Button
                    variant="outline"
                    size="sm"
                    disabled={linking}
                    onClick={() => void link()}
                  >
                    {linking && <Spinner data-icon="inline-start" />}
                    {linking ? 'Linking…' : `Link ${sso.displayName}`}
                  </Button>
                )
              }
            />
          )}
        </SettingsRows>
      </SettingsSection>
      {passwordAllowed &&
        editingPassword &&
        passwordForm(() => setEditingPassword(false))}
      {sso && !sso.linked && (
        <ConfirmPasswordDialog
          provider={sso.displayName}
          open={confirming}
          onOpenChange={setConfirming}
        />
      )}
    </div>
  );
}

function ConfirmPasswordDialog({
  provider,
  open,
  onOpenChange,
}: {
  provider: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const [password, setPassword] = useState('');
  const [pending, setPending] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    if (open) return;
    setPassword('');
    setError('');
  }, [open]);

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (pending) return;
    setPending(true);
    setError('');
    try {
      if ((await beginLink(password)) === 'redirected') return;
      setError('Link failed. Try again.');
    } catch (e) {
      setError(
        await extractErrorDetail(e, 'Link failed. Check the current password.'),
      );
      setPassword('');
    }
    setPending(false);
  }

  return (
    <Dialog open={open} onOpenChange={(next) => !pending && onOpenChange(next)}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Confirm your password</DialogTitle>
          <DialogDescription>
            You signed in more than 10 minutes ago. Enter your password to link{' '}
            {provider}.
          </DialogDescription>
        </DialogHeader>
        <form onSubmit={submit} className="flex flex-col gap-4">
          <Field data-invalid={error ? true : undefined}>
            <FieldLabel htmlFor="link-password">Current password</FieldLabel>
            <Input
              id="link-password"
              type="password"
              autoComplete="current-password"
              required
              value={password}
              disabled={pending}
              aria-invalid={error ? true : undefined}
              aria-describedby={error ? 'link-password-error' : undefined}
              onChange={(event) => setPassword(event.target.value)}
            />
            {error && <FieldError id="link-password-error">{error}</FieldError>}
          </Field>
          <DialogFooter>
            <DialogClose asChild>
              <Button type="button" variant="outline" disabled={pending}>
                Cancel
              </Button>
            </DialogClose>
            <Button type="submit" disabled={pending}>
              {pending && <Spinner data-icon="inline-start" />}
              {pending ? 'Continuing…' : 'Continue'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
