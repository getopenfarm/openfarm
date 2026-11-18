import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { afterEach, expect, it, vi } from 'vitest';
import { AccessContext, AdministratorContext } from '@/lib/auth/AccessContext';
import { AuthenticationSettingsPage } from './AuthenticationSettingsPage';
import { SettingsLayout } from './SettingsLayout';

afterEach(() => vi.unstubAllGlobals());
function setup({ failLoad = false, administrator = true } = {}) {
  let data = {
    revision: 'first',
    oidcEnabled: false,
    internalAuthenticationEnabled: true,
    displayName: 'authentik',
    authority: 'https://identity.example/application/o/farm/',
    clientId: 'farm',
    clientCredentialConfigured: true,
    sessionHours: 8,
    recoveryUserIds: [] as string[],
    jitEnabled: false,
    requireVerifiedEmail: true,
    scimEnabled: false,
    scimCredentialConfigured: false,
    scimRequestsPerMinute: 300,
    defaultGroup: 'Viewers',
    groupMappings: [] as { externalGroup: string; localGroup: string }[],
    recoveryAccounts: [{ id: 'admin-id', username: 'admin' }],
    groups: [
      { id: 'viewers', name: 'Viewers' },
      { id: 'operators', name: 'Operators' },
    ],
    callbackUrl: 'https://farm.example/api/v1/auth/sso/callback',
    logoutUrl: 'https://farm.example/api/v1/auth/sso/backchannel-logout',
    scimUrl: 'https://farm.example/scim/v2',
    recoveryMode: false,
  };
  let failSave = false;
  const writes: { revision: string; settings: Record<string, unknown> }[] = [];
  const fetch = vi.fn(async (_url: string, init?: RequestInit) => {
    if (failLoad) return new Response('{}', { status: 500 });
    if (init?.method === 'PUT') {
      const body = JSON.parse(String(init.body));
      writes.push(body);
      if (failSave)
        return Response.json(
          { detail: 'Settings changed. Reload and try again.' },
          { status: 409 },
        );
      data = {
        ...data,
        ...body.settings,
        clientSecret: undefined,
        scimToken: undefined,
        revision: 'second',
        clientCredentialConfigured: true,
        scimCredentialConfigured:
          !!body.settings.scimToken || data.scimCredentialConfigured,
        groupMappings: Object.entries(body.settings.groupMappings).map(
          ([externalGroup, localGroup]) => ({
            externalGroup,
            localGroup: String(localGroup),
          }),
        ),
      };
    }
    return Response.json(data);
  });
  vi.stubGlobal('fetch', fetch);
  const router = createMemoryRouter(
    [
      {
        path: '/settings',
        element: <SettingsLayout />,
        children: [
          { path: 'authentication', element: <AuthenticationSettingsPage /> },
        ],
      },
    ],
    { initialEntries: ['/settings/authentication'] },
  );
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  render(
    <QueryClientProvider client={client}>
      <AccessContext.Provider value={new Set(['users:manage'])}>
        <AdministratorContext.Provider value={administrator}>
          <RouterProvider router={router} />
        </AdministratorContext.Provider>
      </AccessContext.Provider>
    </QueryClientProvider>,
  );
  return {
    writes,
    fetch,
    failSave: () => {
      failSave = true;
    },
  };
}

it('uses settings navigation and retains stored credentials on save', async () => {
  const user = userEvent.setup();
  const { writes } = setup();
  expect(
    await screen.findByRole('link', { name: 'Authentication' }),
  ).toHaveAttribute('aria-current', 'page');
  expect(screen.queryByLabelText('Client credential')).not.toBeInTheDocument();
  await user.click(
    await screen.findByRole('switch', { name: 'OIDC authentication' }),
  );
  expect(screen.getByRole('button', { name: 'Test sign-in' })).toBeDisabled();
  expect(await screen.findByLabelText('Client credential')).toHaveValue('');
  expect(screen.getByLabelText('Client credential')).toHaveAttribute(
    'type',
    'password',
  );

  await user.click(screen.getByRole('button', { name: 'Save' }));
  await waitFor(() => expect(writes).toHaveLength(1));
  expect(writes[0].settings).toMatchObject({
    oidcEnabled: true,
    clientSecret: null,
    scimToken: null,
  });
  await waitFor(() =>
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled(),
  );
  expect(screen.getByRole('button', { name: 'Test sign-in' })).toBeEnabled();
  await user.click(screen.getByText('Advanced OIDC settings'));
  expect(
    screen.getByText('https://farm.example/api/v1/auth/sso/backchannel-logout'),
  ).toBeInTheDocument();
});

it('requires a recovery administrator before enforcing SSO', async () => {
  const user = userEvent.setup();
  const { writes } = setup();
  await user.click(
    await screen.findByRole('switch', { name: 'OIDC authentication' }),
  );
  await user.click(
    screen.getByRole('switch', { name: 'Internal authentication' }),
  );
  await user.click(screen.getByRole('button', { name: 'Save' }));
  expect(
    await screen.findByText(
      'Select at least one local recovery administrator.',
    ),
  ).toBeInTheDocument();
  expect(writes).toHaveLength(0);
  screen.getByRole('checkbox', { name: 'admin' }).focus();
  await user.keyboard(' ');
  await user.click(screen.getByRole('button', { name: 'Save' }));
  await waitFor(() => expect(writes).toHaveLength(1));
  expect(writes[0].settings).toMatchObject({
    internalAuthenticationEnabled: false,
    recoveryUserIds: ['admin-id'],
  });
});

it('generates a separate SCIM credential and saves group mappings', async () => {
  const user = userEvent.setup();
  const { writes } = setup();
  await user.click(
    await screen.findByRole('switch', { name: 'OIDC authentication' }),
  );
  await user.click(
    screen.getByRole('switch', { name: 'Directory provisioning (SCIM)' }),
  );
  await user.click(screen.getByRole('button', { name: 'Generate credential' }));
  expect(
    (screen.getByLabelText('Provisioning credential') as HTMLInputElement)
      .value,
  ).toMatch(/^[a-f0-9]{64}$/);
  await user.click(screen.getByRole('button', { name: 'Add group mapping' }));
  await user.type(screen.getByLabelText('External group'), 'farm-operators');
  await user.click(screen.getByRole('button', { name: 'Save' }));
  await waitFor(() => expect(writes).toHaveLength(1));
  expect(writes[0].settings.scimToken).toMatch(/^[a-f0-9]{64}$/);
  expect(writes[0].settings.groupMappings).toEqual({
    'farm-operators': 'Viewers',
  });
  await waitFor(() =>
    expect(screen.getByLabelText('Provisioning credential')).toHaveValue(''),
  );
});

it('preserves edits after a save conflict and clears draft credentials on reset', async () => {
  const user = userEvent.setup();
  const { failSave } = setup();
  await user.click(
    await screen.findByRole('switch', { name: 'OIDC authentication' }),
  );
  await user.click(screen.getByRole('button', { name: 'Save' }));
  await waitFor(() =>
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled(),
  );
  await user.type(
    await screen.findByLabelText('Client credential'),
    'new-credential',
  );
  failSave();
  await user.click(screen.getByRole('button', { name: 'Save' }));
  expect(await screen.findByRole('alert')).toHaveTextContent(
    'Settings changed. Reload and try again.',
  );
  expect(screen.getByLabelText('Client credential')).toHaveValue(
    'new-credential',
  );
  await user.click(screen.getByRole('button', { name: 'Reset changes' }));
  expect(screen.getByLabelText('Client credential')).toHaveValue('');
  expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
});

it('shows load errors with retry and hides the administrator navigation from delegated users', async () => {
  setup({ failLoad: true, administrator: false });
  expect(
    await screen.findByText('Could not load authentication settings'),
  ).toBeInTheDocument();
  expect(screen.getByRole('button', { name: /retry/i })).toBeInTheDocument();
  expect(
    screen.queryByRole('link', { name: 'Authentication' }),
  ).not.toBeInTheDocument();
});
