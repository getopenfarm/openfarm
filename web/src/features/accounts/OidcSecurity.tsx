import { useQuery } from '@tanstack/react-query';
import { type FormEvent, useState } from 'react';
import { useSearchParams } from 'react-router';
import { ErrorState } from '@/components/empty-state/EmptyState';
import { Alert, AlertDescription } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Skeleton } from '@/components/ui/skeleton';
import { Spinner } from '@/components/ui/spinner';
import {
  SettingsRow,
  SettingsRows,
  SettingsSection,
} from '@/features/settings/SettingsParts';
import { apiUrl } from '@/lib/api/apiUrl';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
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

export function OidcSecurity({ children }: { children: React.ReactNode }) {
  const [params] = useSearchParams();
  const [password, setPassword] = useState('');
  const [pending, setPending] = useState(false);
  const [error, setError] = useState('');
  const identity = useQuery({
    queryKey: ['auth', 'sso', 'identity'],
    queryFn: async () =>
      (await httpClient<{ data: Identity }>('/api/v1/auth/sso/identity')).data,
    retry: false,
  });
  async function link(event: FormEvent) {
    event.preventDefault();
    if (pending) return;
    setPending(true);
    setError('');
    try {
      // Issue antiforgery proof for the same authenticated user as the linking POST.
      const csrf = await httpClient<{ data: { token: string } }>(
        '/api/v1/auth/csrf',
        {
          cache: 'no-store',
        },
      );
      const response = await httpClient<{ data: { redirectUri: string } }>(
        '/api/v1/auth/sso/link',
        {
          method: 'POST',
          headers: { 'X-CSRF-TOKEN': csrf.data.token },
          body: JSON.stringify({ password }),
        },
      );
      setPassword('');
      window.location.assign(apiUrl(response.data.redirectUri));
    } catch (e) {
      setError(
        await extractErrorDetail(e, 'Link failed. Check the current password.'),
      );
      setPending(false);
    }
  }
  return (
    <>
      {identity.isPending ? (
        <Skeleton className="h-24" />
      ) : identity.isError ? (
        <ErrorState
          title="Load failed"
          onRetry={() => void identity.refetch()}
        />
      ) : (
        identity.data?.enabled && (
          <SettingsSection
            title="Single sign-on"
            description={`Link this account to ${identity.data.displayName}. Permissions do not change.`}
          >
            {params.get('sso') === 'linked' && (
              <Alert className="mb-4">
                <AlertDescription>Account linked.</AlertDescription>
              </Alert>
            )}
            {params.get('sso') === 'failed' && (
              <Alert variant="destructive" className="mb-4">
                <AlertDescription>
                  {(Object.hasOwn(
                    linkFailureMessages,
                    params.get('ssoReason') ?? '',
                  )
                    ? linkFailureMessages[params.get('ssoReason') ?? '']
                    : undefined) ??
                    'Link failed. Start again, or contact an administrator.'}
                </AlertDescription>
              </Alert>
            )}
            {identity.data.linked ? (
              <p className="text-sm">Linked to {identity.data.displayName}.</p>
            ) : (
              <form onSubmit={link}>
                <SettingsRows>
                  <SettingsRow label="Current password" htmlFor="oidc-password">
                    <Input
                      id="oidc-password"
                      type="password"
                      autoComplete="current-password"
                      required
                      value={password}
                      disabled={pending}
                      onChange={(event) => setPassword(event.target.value)}
                    />
                  </SettingsRow>
                </SettingsRows>
                {error && (
                  <Alert variant="destructive" className="mt-4">
                    <AlertDescription>{error}</AlertDescription>
                  </Alert>
                )}
                <Button type="submit" disabled={pending} className="mt-4">
                  {pending && <Spinner data-icon="inline-start" />}
                  {pending ? 'Linking…' : `Link ${identity.data.displayName}`}
                </Button>
              </form>
            )}
          </SettingsSection>
        )
      )}
      {identity.data?.passwordAllowed !== false ? (
        children
      ) : (
        <SettingsSection
          title="Password sign-in disabled"
          description="Manage your credentials in the provider."
        >
          <p className="text-sm">
            Contact an administrator if you cannot sign in.
          </p>
        </SettingsSection>
      )}
    </>
  );
}
