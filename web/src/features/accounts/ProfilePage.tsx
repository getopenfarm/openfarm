import { useQueryClient } from '@tanstack/react-query';
import { type FormEvent, useState } from 'react';
import { Link, useSearchParams } from 'react-router';
import { toast } from 'sonner';
import { ErrorState } from '@/components/empty-state/EmptyState';
import {
  SideNavPage,
  sideNavItemClass,
} from '@/components/side-nav-page/SideNavPage';
import { Alert, AlertDescription } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { FieldError } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { Skeleton } from '@/components/ui/skeleton';
import { Spinner } from '@/components/ui/spinner';
import {
  SettingsRow,
  SettingsRows,
  SettingsSection,
} from '@/features/settings/SettingsParts';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import { useOpenFarmApiEndpointsAuthListConnectedAppsEndpoint } from '@/lib/api/generated/auth/auth';
import type { ConnectedAppsResponse } from '@/lib/api/generated/endpoints.schemas';
import { httpClient } from '@/lib/api/httpClient';
import { type CurrentUser, useIdentity } from '@/lib/auth/permissions';
import { sessionsKey, useUpdateProfile, whoAmIKey } from './api';
import { ConnectAssistantCard } from './ConnectAssistantCard';
import { SessionList } from './SessionList';
import { TokenList } from './TokenList';

const tabs = [
  { id: 'profile', label: 'Profile' },
  { id: 'security', label: 'Security' },
  { id: 'sessions', label: 'Sessions' },
  { id: 'tokens', label: 'Access tokens' },
  { id: 'apps', label: 'Connected apps' },
] as const;
type Tab = (typeof tabs)[number]['id'];

export function ProfilePage() {
  const [params, setParams] = useSearchParams();
  const asked = params.get('tab');
  const tab: Tab = tabs.some((t) => t.id === asked)
    ? (asked as Tab)
    : 'profile';
  const identity = useIdentity();
  return (
    <SideNavPage
      title="My profile"
      description="Manage your account details and access"
      navLabel="Profile"
      nav={tabs.map((t) => (
        <button
          key={t.id}
          type="button"
          aria-current={tab === t.id ? 'page' : undefined}
          className={sideNavItemClass(tab === t.id)}
          // The tab is page state, not a new page to go back to.
          onClick={() =>
            setParams(t.id === 'profile' ? {} : { tab: t.id }, {
              replace: true,
            })
          }
        >
          {t.label}
        </button>
      ))}
    >
      {tab === 'profile' &&
        (identity.data ? (
          <ProfileTab user={identity.data} />
        ) : identity.isError ? (
          <ErrorState
            title="Could not load profile"
            onRetry={() => identity.refetch()}
          />
        ) : (
          <Skeleton className="h-48 max-w-[640px]" />
        ))}
      {tab === 'security' && <SecurityTab />}
      {tab === 'tokens' && (
        <SettingsSection
          title="Access tokens"
          description={
            <>
              Personal access tokens let a script or an AI assistant use
              OpenFarm as you, with only the permissions you give each one. Send
              one in an <code>Authorization: Bearer</code> header.{' '}
              <Link to="/profile?tab=apps" replace className="underline">
                Connect an AI assistant
              </Link>
            </>
          }
        >
          <TokenList />
        </SettingsSection>
      )}
      {tab === 'apps' && <ConnectedAppsTab />}
      {tab === 'sessions' && (
        <SettingsSection
          title="Sessions"
          description="Browsers signed in as you. A session ends 14 days after sign-in."
        >
          <SessionList />
        </SettingsSection>
      )}
    </SideNavPage>
  );
}

function ConnectedAppsTab() {
  const query = useOpenFarmApiEndpointsAuthListConnectedAppsEndpoint();
  const apps = query.data?.data as ConnectedAppsResponse | undefined;
  return (
    <SettingsSection
      title="Connected apps"
      description="Apps that use OpenFarm as you, with the access you allowed. Revoking access disconnects the app at once."
    >
      {apps ? (
        <ConnectAssistantCard
          mcpUrl={apps.mcpUrl}
          signInEnabled={apps.signInEnabled ?? false}
          defaultOpen
        />
      ) : query.isError ? (
        <ErrorState
          title="Could not load connected apps"
          onRetry={() => query.refetch()}
        />
      ) : (
        <Skeleton className="h-48" />
      )}
    </SettingsSection>
  );
}

function ProfileTab({ user }: { user: CurrentUser }) {
  const [name, setName] = useState(user.displayName ?? '');
  const [email, setEmail] = useState(user.email);
  const [username, setUsername] = useState(user.username);
  const [error, setError] = useState('');
  const update = useUpdateProfile();

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (update.isPending) return;
    setError('');
    try {
      await update.mutateAsync({ name, email, username });
      toast.success('Profile saved.');
    } catch (e) {
      setError(await extractErrorDetail(e, 'Could not update profile.'));
    }
  }

  return (
    <SettingsSection
      title="Profile"
      description="Update your display name, email, and username"
    >
      <form onSubmit={submit}>
        <fieldset disabled={update.isPending} className="contents">
          <SettingsRows>
            <SettingsRow label="Display name" htmlFor="profile-name">
              <Input
                id="profile-name"
                placeholder="Your full name"
                value={name}
                onChange={(e) => setName(e.target.value)}
              />
            </SettingsRow>
            <SettingsRow label="Email" htmlFor="profile-email">
              <Input
                id="profile-email"
                type="email"
                required
                value={email}
                onChange={(e) => setEmail(e.target.value)}
              />
            </SettingsRow>
            <SettingsRow label="Username" htmlFor="profile-username">
              <Input
                id="profile-username"
                required
                autoComplete="username"
                value={username}
                onChange={(e) => setUsername(e.target.value)}
              />
            </SettingsRow>
          </SettingsRows>
        </fieldset>
        {error && (
          <Alert variant="destructive" className="mt-4">
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        )}
        <Button type="submit" className="mt-4" disabled={update.isPending}>
          {update.isPending && <Spinner data-icon="inline-start" />}
          {update.isPending ? 'Saving…' : 'Save'}
        </Button>
      </form>
    </SettingsSection>
  );
}

function SecurityTab() {
  const client = useQueryClient();
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [error, setError] = useState('');
  const [pending, setPending] = useState(false);
  const mismatch =
    confirmPassword.length > 0 && newPassword !== confirmPassword;

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (pending || mismatch) return;
    setError('');
    setPending(true);
    try {
      // The server keeps this session and signs out every other one.
      await httpClient('/api/v1/auth/change-password', {
        method: 'POST',
        body: JSON.stringify({ currentPassword, newPassword }),
      });
      toast.success('Password changed.');
      setCurrentPassword('');
      setNewPassword('');
      setConfirmPassword('');
      void client.invalidateQueries({ queryKey: whoAmIKey });
      void client.invalidateQueries({ queryKey: sessionsKey() });
    } catch (e) {
      setError(await extractErrorDetail(e, 'Could not change password.'));
    } finally {
      setPending(false);
    }
  }

  return (
    <SettingsSection
      title="Change password"
      description="Choose a strong password of at least 8 characters"
    >
      <form onSubmit={submit}>
        <fieldset disabled={pending} className="contents">
          <SettingsRows>
            <SettingsRow label="Current password" htmlFor="current-password">
              <Input
                id="current-password"
                type="password"
                required
                autoComplete="current-password"
                value={currentPassword}
                onChange={(e) => setCurrentPassword(e.target.value)}
              />
            </SettingsRow>
            <SettingsRow label="New password" htmlFor="new-password">
              <Input
                id="new-password"
                type="password"
                required
                minLength={8}
                autoComplete="new-password"
                value={newPassword}
                onChange={(e) => setNewPassword(e.target.value)}
              />
            </SettingsRow>
            <SettingsRow
              label="Confirm new password"
              htmlFor="confirm-password"
              invalid={mismatch}
              disabled={pending}
            >
              <Input
                id="confirm-password"
                type="password"
                required
                autoComplete="new-password"
                aria-invalid={mismatch}
                aria-describedby={
                  mismatch ? 'confirm-password-error' : undefined
                }
                value={confirmPassword}
                onChange={(e) => setConfirmPassword(e.target.value)}
              />
              {mismatch && (
                <FieldError id="confirm-password-error">
                  Passwords do not match.
                </FieldError>
              )}
            </SettingsRow>
          </SettingsRows>
        </fieldset>
        {error && (
          <Alert variant="destructive" className="mt-4">
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        )}
        <Button type="submit" className="mt-4" disabled={mismatch || pending}>
          {pending && <Spinner data-icon="inline-start" />}
          {pending ? 'Changing password…' : 'Change password'}
        </Button>
      </form>
    </SettingsSection>
  );
}
