import { useQueryClient } from '@tanstack/react-query';
import { type FormEvent, useState } from 'react';
import { useSearchParams } from 'react-router';
import { toast } from 'sonner';
import {
  SideNavPage,
  sideNavItemClass,
} from '@/components/side-nav-page/SideNavPage';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Skeleton } from '@/components/ui/skeleton';
import {
  SettingsRow,
  SettingsRows,
  SettingsSection,
} from '@/features/settings/SettingsParts';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import { httpClient } from '@/lib/api/httpClient';
import { type CurrentUser, useIdentity } from '@/lib/auth/permissions';
import { sessionsKey, useUpdateProfile, whoAmIKey } from './api';
import { SessionList } from './SessionList';

const tabs = [
  { id: 'profile', label: 'Profile' },
  { id: 'security', label: 'Security' },
  { id: 'sessions', label: 'Sessions' },
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
      title="My Profile"
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
        ) : (
          <Skeleton className="h-48 max-w-[640px]" />
        ))}
      {tab === 'security' && <SecurityTab />}
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

function ProfileTab({ user }: { user: CurrentUser }) {
  const [name, setName] = useState(user.displayName ?? '');
  const [email, setEmail] = useState(user.email);
  const [username, setUsername] = useState(user.username);
  const [error, setError] = useState('');
  const update = useUpdateProfile();

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError('');
    try {
      await update.mutateAsync({ name, email, username });
      toast.success('Profile updated');
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
        {error && (
          <p role="alert" className="mx-4 mt-2.5 text-xs text-destructive">
            {error}
          </p>
        )}
        <Button type="submit" className="mt-4" disabled={update.isPending}>
          Save changes
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
    setError('');
    setPending(true);
    try {
      // The server keeps this session and signs out every other one.
      await httpClient('/api/v1/auth/change-password', {
        method: 'POST',
        body: JSON.stringify({ currentPassword, newPassword }),
      });
      toast.success('Password changed');
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
      title="Change Password"
      description="Choose a strong password of at least 8 characters"
    >
      <form onSubmit={submit}>
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
          <SettingsRow label="Confirm new password" htmlFor="confirm-password">
            <Input
              id="confirm-password"
              type="password"
              required
              autoComplete="new-password"
              aria-invalid={mismatch}
              value={confirmPassword}
              onChange={(e) => setConfirmPassword(e.target.value)}
            />
          </SettingsRow>
          {mismatch && (
            <p className="py-3 text-xs text-destructive">
              Passwords do not match.
            </p>
          )}
        </SettingsRows>
        {error && (
          <p role="alert" className="mx-4 mt-2.5 text-xs text-destructive">
            {error}
          </p>
        )}
        <Button type="submit" className="mt-4" disabled={mismatch || pending}>
          Change password
        </Button>
      </form>
    </SettingsSection>
  );
}
