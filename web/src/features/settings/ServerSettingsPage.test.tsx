import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { afterEach, expect, it, vi } from 'vitest';
import { AccessContext, AdministratorContext } from '@/lib/auth/AccessContext';
import {
  AiAssistantsSettingsPage,
  ServerSettingsPage,
} from './ServerSettingsPage';
import { SettingsLayout } from './SettingsLayout';

afterEach(() => vi.unstubAllGlobals());

function setup({
  kind = 'mcp',
  managed = false,
  mcpEnabledOverride = null as boolean | null,
  failLoad = false,
  administrator = true,
} = {}) {
  let data = {
    publicUrl: null as string | null,
    mcpMode: 'Disabled',
    revision: 'revision-one',
    publicUrlManaged: managed,
    mcpModeManaged: managed,
    mcpEnabledOverride,
  };
  const writes: Record<string, unknown>[] = [];
  let failSave = false;
  const fetch = vi.fn(async (_url: string, init?: RequestInit) => {
    if (failLoad) return new Response('{}', { status: 500 });
    if (init?.method === 'PUT') {
      const body = JSON.parse(String(init.body));
      writes.push(body);
      if (failSave)
        return Response.json(
          { detail: 'Password was not accepted.' },
          { status: 422 },
        );
      data = {
        ...data,
        publicUrl: body.publicUrl ?? data.publicUrl,
        mcpMode: body.mode ?? data.mcpMode,
        revision: 'revision-two',
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
          { path: 'server', element: <ServerSettingsPage /> },
          {
            path: 'integrations/ai-assistants',
            element: <AiAssistantsSettingsPage />,
          },
        ],
      },
    ],
    {
      initialEntries: [
        kind === 'mcp'
          ? '/settings/integrations/ai-assistants'
          : '/settings/server',
      ],
    },
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
    recover: () => {
      failSave = false;
    },
  };
}

it('defaults to disabled, selects a mode by keyboard, requires reauthentication and clears the password after saving', async () => {
  const user = userEvent.setup();
  const { writes } = setup();
  const mode = await screen.findByRole('combobox', { name: 'MCP access' });
  expect(mode).toHaveTextContent('Disabled');
  expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
  mode.focus();
  await user.keyboard('{ArrowDown}');
  await user.keyboard('{ArrowDown}{Enter}');
  expect(mode).toHaveTextContent('Access tokens only');
  await user.click(screen.getByRole('button', { name: 'Save' }));
  expect(
    await screen.findByText('Enter your current password.'),
  ).toBeInTheDocument();
  expect(writes).toHaveLength(0);
  await user.type(
    screen.getByLabelText('Current password'),
    'administrator-password',
  );
  await user.click(screen.getByRole('button', { name: 'Save' }));
  await waitFor(() => expect(writes).toHaveLength(1));
  expect(writes[0]).toEqual({
    revision: 'revision-one',
    mode: 'AccessTokens',
    password: 'administrator-password',
  });
  await waitFor(() =>
    expect(screen.getByLabelText('Current password')).toHaveValue(''),
  );
  expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
});

it('validates the public URL and saves it without changing MCP mode', async () => {
  const user = userEvent.setup();
  const { writes } = setup({ kind: 'server' });
  const input = await screen.findByLabelText('Public URL');
  await user.type(input, 'https://farm.test/path');
  await user.type(
    screen.getByLabelText('Current password'),
    'administrator-password',
  );
  await user.click(screen.getByRole('button', { name: 'Save' }));
  expect(
    await screen.findByText(/Enter an HTTP or HTTPS URL/),
  ).toBeInTheDocument();
  expect(writes).toHaveLength(0);
  await user.clear(input);
  await user.type(input, 'https://farm.test');
  await user.click(screen.getByRole('button', { name: 'Save' }));
  await waitFor(() => expect(writes).toHaveLength(1));
  expect(writes[0]).toEqual({
    revision: 'revision-one',
    publicUrl: 'https://farm.test',
    password: 'administrator-password',
  });
});

it('keeps deployment-controlled fields read-only and names the enabled override', async () => {
  setup({ managed: true, mcpEnabledOverride: false });
  expect(
    await screen.findByRole('combobox', { name: 'MCP access' }),
  ).toBeDisabled();
  expect(screen.getByRole('status')).toHaveTextContent('MCP_ENABLED=false');
  expect(screen.queryByLabelText('Current password')).not.toBeInTheDocument();
  expect(
    screen.queryByRole('button', { name: 'Save' }),
  ).not.toBeInTheDocument();
});

it('reports a save failure, clears the password and permits a retry', async () => {
  const user = userEvent.setup();
  const state = setup({ kind: 'server' });
  state.failSave();
  await user.type(
    await screen.findByLabelText('Public URL'),
    'https://farm.test',
  );
  await user.type(screen.getByLabelText('Current password'), 'incorrect');
  await user.click(screen.getByRole('button', { name: 'Save' }));
  expect(await screen.findByRole('alert')).toHaveTextContent(
    'Password was not accepted.',
  );
  await waitFor(() =>
    expect(screen.getByLabelText('Current password')).toHaveValue(''),
  );
  state.recover();
  await user.type(screen.getByLabelText('Current password'), 'correct');
  await user.click(screen.getByRole('button', { name: 'Save' }));
  await waitFor(() => expect(state.writes).toHaveLength(2));
  expect(state.writes[1].password).toBe('correct');
});

it('reports loading errors and hides installation links for delegated non-administrators', async () => {
  setup({ failLoad: true, administrator: false });
  expect(
    await screen.findByText('Could not load server settings'),
  ).toBeInTheDocument();
  expect(
    screen.queryByRole('link', { name: 'Server' }),
  ).not.toBeInTheDocument();
  expect(
    screen.queryByRole('link', { name: 'AI assistants' }),
  ).not.toBeInTheDocument();
});
