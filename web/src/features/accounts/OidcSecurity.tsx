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
    'Could not load or use the sign-in provider configuration. Check the Issuer URL and make sure OpenFarm can reach the provider. Administrators can check the OpenFarm server logs for details.',
  provider_response:
    'The provider sign-in response could not be verified. Ask an administrator to check the issuer, client configuration, signing key, and OpenFarm server logs.',
  fresh_authentication:
    'The provider did not confirm a recent sign-in. Sign out of your sign-in provider, then start linking again. An administrator should check the provider authentication flow and server clocks if this continues.',
  browser_proof:
    'The linking browser proof is missing or does not match. Start linking again in the same browser and use the same OpenFarm hostname throughout.',
  session_changed:
    'Your original OpenFarm session changed or expired. Sign in to your local OpenFarm account again, then start linking from this browser.',
  expired:
    'The linking request expired or could not be read. Start a new linking request from My profile.',
  request_replaced:
    'The linking request was already used or replaced. Start a new linking request and complete only that attempt.',
  configuration_changed:
    'The sign-in settings changed during linking. Start a new linking request.',
  already_linked:
    'This OpenFarm account is already linked to the provider. Reload My profile to check its status.',
  identity_conflict:
    'This provider identity is already linked to an OpenFarm account. Contact an administrator to check the account association.',
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
        await extractErrorDetail(
          e,
          'Could not start linking. Check your current password and try again.',
        ),
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
          title="Could not load sign-in methods"
          onRetry={() => void identity.refetch()}
        />
      ) : (
        identity.data?.enabled && (
          <SettingsSection
            title="Single sign-on"
            description={`Connect your existing OpenFarm account to ${identity.data.displayName}. Your OpenFarm permissions stay the same.`}
          >
            {params.get('sso') === 'linked' && (
              <Alert className="mb-4">
                <AlertDescription>Sign-in provider linked.</AlertDescription>
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
                    'Could not link this identity. Start again, or contact an administrator.'}
                </AlertDescription>
              </Alert>
            )}
            {identity.data.linked ? (
              <p className="text-sm">Linked to {identity.data.displayName}.</p>
            ) : (
              <form onSubmit={link}>
                <SettingsRows>
                  <SettingsRow
                    label="Confirm local password"
                    htmlFor="oidc-password"
                  >
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
                  {pending
                    ? 'Connecting…'
                    : `Link ${identity.data.displayName}`}
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
          title="Password login disabled"
          description="Use your organization’s sign-in provider to manage your credentials."
        >
          <p className="text-sm">
            Contact an administrator if you cannot sign in.
          </p>
        </SettingsSection>
      )}
    </>
  );
}
