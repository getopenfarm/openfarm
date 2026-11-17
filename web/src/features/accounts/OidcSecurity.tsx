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
                  Could not link this identity. Start again, or contact an
                  administrator.
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
