import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { afterEach, expect, it, vi } from 'vitest';
import { AccessContext, AdministratorContext } from '@/lib/auth/AccessContext';
import { AuthenticationSettingsPage } from './AuthenticationSettingsPage';
import { SettingsLayout } from './SettingsLayout';

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});
function setup({
  failLoad = false,
  administrator = true,
  configured = false,
} = {}) {
  let data = {
    revision: 'first',
    oidcEnabled: configured,
    internalAuthenticationEnabled: true,
    displayName: '',
    authority: 'https://identity.example/application/o/farm/',
    clientId: 'farm',
    clientCredentialConfigured: true,
    recoveryUserIds: [] as string[],
    jitEnabled: configured,
    requireVerifiedEmail: true,
    defaultGroup: 'Viewers',
    groupMappings: [] as { externalGroup: string; group: string }[],
    groupsClaim: 'groups',
    additionalScopes: '',
    recoveryAccounts: [{ id: 'admin-id', username: 'admin' }],
    groups: [
      { id: 'administrators', name: 'Administrators', isProtected: true },
      { id: 'viewers', name: 'Viewers', isProtected: false },
      { id: 'operators', name: 'Operators', isProtected: false },
    ],
    callbackUrl: 'https://farm.example/api/v1/auth/sso/callback',
    logoutUrl: 'https://farm.example/api/v1/auth/sso/backchannel-logout',
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
        revision: 'second',
        clientCredentialConfigured: true,
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
  expect(await screen.findByLabelText('Client credential')).toHaveValue('');
  expect(screen.getByLabelText('Client credential')).toHaveAttribute(
    'type',
    'password',
  );
  // A saved credential shows dots (never the value); blank keeps it.
  expect(screen.getByLabelText('Client credential')).toHaveAttribute(
    'placeholder',
    '••••••••••••',
  );

  await user.click(screen.getByRole('button', { name: 'Save' }));
  await waitFor(() => expect(writes).toHaveLength(1));
  expect(writes[0].settings).toMatchObject({
    oidcEnabled: true,
    displayName: '',
    clientSecret: null,
  });
  await waitFor(() =>
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled(),
  );
  expect(screen.queryByText('Advanced OIDC settings')).not.toBeInTheDocument();
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
  expect(await screen.findByText('Load failed')).toBeInTheDocument();
  expect(screen.getByRole('button', { name: /retry/i })).toBeInTheDocument();
  expect(
    screen.queryByRole('link', { name: 'Authentication' }),
  ).not.toBeInTheDocument();
});

it('reveals OIDC and provisioning controls only while their features are enabled', async () => {
  const user = userEvent.setup();
  setup();
  const oidc = await screen.findByRole('switch', {
    name: 'OIDC authentication',
  });
  expect(
    screen.queryByText('Account provisioning', { exact: true }),
  ).not.toBeInTheDocument();
  expect(screen.queryByLabelText('Client ID')).not.toBeInTheDocument();
  await user.click(oidc);
  expect(
    screen.getByText('Account provisioning', { exact: true }),
  ).toBeInTheDocument();
  expect(
    screen.queryByRole('switch', { name: 'Require verified email' }),
  ).not.toBeInTheDocument();
  expect(
    screen.queryByRole('combobox', { name: 'Default group' }),
  ).not.toBeInTheDocument();
  expect(
    screen.queryByLabelText('Requests per minute'),
  ).not.toBeInTheDocument();
  const jit = screen.getByRole('switch', {
    name: 'Create accounts on first sign-in (JIT)',
  });
  jit.focus();
  await user.keyboard(' ');
  expect(jit).toBeChecked();
  expect(
    screen.getByRole('switch', { name: 'Require verified email' }),
  ).toBeChecked();
  expect(
    screen.getByRole('combobox', { name: 'Default group' }),
  ).toBeInTheDocument();
  expect(
    screen.queryByText('Advanced provisioning settings'),
  ).not.toBeInTheDocument();
  await user.click(oidc);
  expect(
    screen.queryByText('Account provisioning', { exact: true }),
  ).not.toBeInTheDocument();
  expect(
    screen.queryByLabelText('Provisioning credential'),
  ).not.toBeInTheDocument();
});

it('preserves the complete provisioning draft across an unsaved OIDC off/on cycle', async () => {
  const user = userEvent.setup();
  const { writes } = setup();
  const oidc = await screen.findByRole('switch', {
    name: 'OIDC authentication',
  });
  await user.click(oidc);
  await user.click(
    screen.getByRole('switch', {
      name: 'Create accounts on first sign-in (JIT)',
    }),
  );
  await user.type(
    screen.getByLabelText('Client credential'),
    'draft-client-credential',
  );
  await user.click(oidc);
  await user.click(oidc);
  expect(
    screen.getByRole('switch', {
      name: 'Create accounts on first sign-in (JIT)',
    }),
  ).toBeChecked();
  expect(screen.getByLabelText('Client credential')).toHaveValue(
    'draft-client-credential',
  );
  await user.click(screen.getByRole('button', { name: 'Save' }));
  await waitFor(() => expect(writes).toHaveLength(1));
  expect(writes[0].settings).toMatchObject({
    oidcEnabled: true,
    jitEnabled: true,
    clientSecret: 'draft-client-credential',
  });
});

it('adds, validates, saves and removes provider group mappings', async () => {
  const user = userEvent.setup();
  const { writes } = setup({ configured: true });
  await screen.findByRole('switch', { name: 'OIDC authentication' });
  await user.click(screen.getByRole('button', { name: 'Add mapping' }));
  // An incomplete row blocks Save with an error on the row.
  await user.click(screen.getByRole('button', { name: 'Save' }));
  expect(
    await screen.findByText('Enter the provider group name.'),
  ).toBeInTheDocument();
  expect(writes).toHaveLength(0);
  await user.type(screen.getByLabelText('Claim value 1'), ' farm-ops ');
  await user.click(screen.getByRole('combobox', { name: 'OpenFarm group 1' }));
  await user.click(await screen.findByRole('option', { name: 'Operators' }));
  await user.click(screen.getByRole('button', { name: 'Add mapping' }));
  await user.type(screen.getByLabelText('Claim value 2'), 'farm-ops');
  await user.click(screen.getByRole('combobox', { name: 'OpenFarm group 2' }));
  await user.click(await screen.findByRole('option', { name: 'Operators' }));
  await user.click(screen.getByRole('button', { name: 'Save' }));
  expect(await screen.findByText('Duplicate mapping.')).toBeInTheDocument();
  await user.click(screen.getByRole('button', { name: 'Remove mapping 2' }));
  await user.click(screen.getByRole('button', { name: 'Save' }));
  await waitFor(() => expect(writes).toHaveLength(1));
  expect(writes[0].settings).toMatchObject({
    jitEnabled: true,
    groupMappings: [{ externalGroup: 'farm-ops', group: 'Operators' }],
  });
  // The saved mapping reloads into its row and can be removed again.
  expect(await screen.findByLabelText('Claim value 1')).toHaveValue('farm-ops');
  await user.click(screen.getByRole('button', { name: 'Remove mapping 1' }));
  await user.click(screen.getByRole('button', { name: 'Save' }));
  await waitFor(() => expect(writes).toHaveLength(2));
  expect(writes[1].settings).toMatchObject({ groupMappings: [] });
});

it('offers a protected group as a mapping target only, and warns about it', async () => {
  const user = userEvent.setup();
  const { writes } = setup({ configured: true });
  await user.click(
    await screen.findByRole('combobox', { name: 'Default group' }),
  );
  expect(
    await screen.findByRole('option', { name: 'Operators' }),
  ).toBeInTheDocument();
  expect(
    screen.queryByRole('option', { name: 'Administrators' }),
  ).not.toBeInTheDocument();
  await user.keyboard('{Escape}');
  await user.click(screen.getByRole('button', { name: 'Add mapping' }));
  await user.type(screen.getByLabelText('Claim value 1'), 'farm-admins');
  await user.click(screen.getByRole('combobox', { name: 'OpenFarm group 1' }));
  expect(
    screen.queryByText(/A protected group has full access/),
  ).not.toBeInTheDocument();
  await user.click(
    await screen.findByRole('option', { name: 'Administrators' }),
  );
  expect(
    await screen.findByText(/A protected group has full access/),
  ).toBeInTheDocument();
  await user.click(screen.getByRole('button', { name: 'Save' }));
  await waitFor(() => expect(writes).toHaveLength(1));
  expect(writes[0].settings).toMatchObject({
    groupMappings: [{ externalGroup: 'farm-admins', group: 'Administrators' }],
  });
});

it('saves a custom groups claim and rejects identity claims', async () => {
  const user = userEvent.setup();
  const { writes } = setup({ configured: true });
  const claim = await screen.findByLabelText('Groups claim');
  expect(claim).toHaveValue('groups');
  await user.clear(claim);
  await user.type(claim, 'email');
  await user.click(screen.getByRole('button', { name: 'Save' }));
  expect(await screen.findByText(/Not an identity claim/)).toBeInTheDocument();
  expect(writes).toHaveLength(0);
  await user.clear(claim);
  await user.type(claim, 'entitlements');
  await user.click(screen.getByRole('button', { name: 'Save' }));
  await waitFor(() => expect(writes).toHaveLength(1));
  expect(writes[0].settings).toMatchObject({ groupsClaim: 'entitlements' });
});

it('saves additional scopes and rejects offline_access', async () => {
  const user = userEvent.setup();
  const { writes } = setup({ configured: true });
  const scopes = await screen.findByLabelText('Additional scopes');
  expect(scopes).toHaveValue('');
  await user.type(scopes, 'entitlements offline_access');
  await user.click(screen.getByRole('button', { name: 'Save' }));
  expect(await screen.findByText(/Not offline_access/)).toBeInTheDocument();
  expect(writes).toHaveLength(0);
  await user.clear(scopes);
  await user.type(scopes, 'entitlements');
  await user.click(screen.getByRole('button', { name: 'Save' }));
  await waitFor(() => expect(writes).toHaveLength(1));
  expect(writes[0].settings).toMatchObject({
    additionalScopes: 'entitlements',
  });
});

it('warns when disabling saved OIDC and saves disabled provisioning without hidden draft errors', async () => {
  const user = userEvent.setup();
  const { writes } = setup({ configured: true });
  const oidc = await screen.findByRole('switch', {
    name: 'OIDC authentication',
  });
  await user.clear(screen.getByLabelText('Client ID'));
  await user.click(oidc);
  expect(screen.getByRole('status')).toHaveTextContent('turns off JIT');
  expect(
    screen.queryByText('Account provisioning', { exact: true }),
  ).not.toBeInTheDocument();
  await user.click(screen.getByRole('button', { name: 'Save' }));
  await waitFor(() => expect(writes).toHaveLength(1));
  expect(writes[0].settings).toMatchObject({
    oidcEnabled: false,
    internalAuthenticationEnabled: true,
    jitEnabled: false,
    clientId: 'farm',
    clientSecret: null,
  });
  await waitFor(() =>
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled(),
  );
});

it('resets the hidden draft and restores the saved enabled features', async () => {
  const user = userEvent.setup();
  setup({ configured: true });
  await user.type(
    await screen.findByLabelText('Client credential'),
    'draft-credential',
  );
  await user.click(screen.getByRole('switch', { name: 'OIDC authentication' }));
  await user.click(screen.getByRole('button', { name: 'Reset changes' }));
  expect(
    screen.getByRole('switch', { name: 'OIDC authentication' }),
  ).toBeChecked();
  expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
});

it('copies both provider addresses using accessible keyboard actions', async () => {
  const user = userEvent.setup();
  setup({ configured: true });
  const copy = vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue();
  const redirect = await screen.findByRole('button', {
    name: 'Copy redirect uri',
  });
  redirect.focus();
  await user.keyboard('{Enter}');
  expect(copy).toHaveBeenLastCalledWith(
    'https://farm.example/api/v1/auth/sso/callback',
  );
  screen.getByRole('button', { name: 'Copy back-channel logout uri' }).focus();
  await user.keyboard(' ');
  expect(copy).toHaveBeenLastCalledWith(
    'https://farm.example/api/v1/auth/sso/backchannel-logout',
  );
});
