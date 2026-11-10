import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { httpClient } from '@/lib/api/httpClient';
import { UserFormPage } from './UserFormPage';
import { UsersGroupsPage } from './UsersGroupsPage';

vi.mock('@/lib/api/httpClient', () => ({ httpClient: vi.fn() }));
const http = vi.mocked(httpClient);

const data: Record<string, unknown> = {
  '/api/v1/admin/users': [
    {
      id: 'u1',
      name: 'Mei Ling',
      email: 'mei@example.com',
      username: 'mei',
      isEnabled: true,
      groups: [{ id: 'g1', name: 'Operators' }],
      extraGrants: [],
      isAdministrator: false,
      sessionCount: 2,
      createdAtUtc: '2026-10-01T00:00:00Z',
    },
  ],
  '/api/v1/admin/groups': [
    {
      id: 'g1',
      name: 'Operators',
      description: '',
      grants: ['printers:list'],
      memberCount: 1,
    },
  ],
  '/api/v1/admin/permissions': {
    areas: [{ key: 'printers', label: 'Printers', noun: 'printers' }],
    permissions: [],
  },
  '/api/v1/admin/users/u1/sessions': [],
  '/api/v1/admin/users/u1/tokens': [],
  '/api/v1/admin/users/u1/apps': [
    {
      id: 's9',
      clientName: 'Claude',
      clientIdSuffix: '9f2a',
      returnsTo: { kind: 'Web', label: 'claude.ai' },
      access: [],
      createdAtUtc: '2026-11-10T00:00:00Z',
      expiresAtUtc: '2026-12-10T00:00:00Z',
    },
  ],
};

beforeEach(() => {
  http.mockReset();
  http.mockImplementation(async (url: string, init?: RequestInit) =>
    init?.method ? undefined : { data: data[url] },
  );
});

function renderAt(path: string) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  const router = createMemoryRouter(
    [
      { path: '/users-groups', element: <UsersGroupsPage /> },
      { path: '/users-groups/users/new', element: <UserFormPage /> },
      { path: '/users-groups/users/:id', element: <UserFormPage /> },
    ],
    { initialEntries: [path] },
  );
  render(
    <QueryClientProvider client={client}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );
  return router;
}

describe('UsersGroupsPage', () => {
  it('lists users and opens a user page on row click', async () => {
    const user = userEvent.setup();
    const router = renderAt('/users-groups');

    expect(await screen.findByText('Mei Ling')).toBeInTheDocument();
    expect(screen.getByText('2 active')).toBeInTheDocument();
    await user.click(screen.getByText('mei@example.com'));

    expect(router.state.location.pathname).toBe('/users-groups/users/u1');
    expect(
      await screen.findByRole('heading', { name: 'Mei Ling' }),
    ).toBeInTheDocument();
  });
});

describe('UserFormPage', () => {
  it('opens the add form from the keyboard and creates a user', async () => {
    const user = userEvent.setup();
    const router = renderAt('/users-groups');

    const add = await screen.findByRole('button', { name: 'Add user' });
    add.focus();
    await user.keyboard('{Enter}');

    expect(router.state.location.pathname).toBe('/users-groups/users/new');
    expect(
      await screen.findByRole('heading', { name: 'Add user' }),
    ).toBeInTheDocument();
    expect(screen.queryByText('User not found')).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Remove' }),
    ).not.toBeInTheDocument();

    await user.type(screen.getByLabelText('Full name'), 'Alex Tan');
    await user.type(screen.getByLabelText('Email'), 'alex@example.com');
    await user.type(screen.getByLabelText('Username'), 'alex');
    await user.type(
      screen.getByLabelText('Temporary password'),
      'initial-password',
    );
    await user.click(screen.getByRole('button', { name: 'Add user' }));

    expect(http).toHaveBeenCalledWith('/api/v1/admin/users', {
      method: 'POST',
      body: JSON.stringify({
        name: 'Alex Tan',
        email: 'alex@example.com',
        username: 'alex',
        groupIds: [],
        extraGrants: [],
        isEnabled: true,
        password: 'initial-password',
      }),
    });
    expect(await screen.findByText('Mei Ling')).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/users-groups');
  });

  it('saves an edit with an inline password reset', async () => {
    const user = userEvent.setup();
    renderAt('/users-groups/users/u1');

    await user.type(
      await screen.findByLabelText('Reset password'),
      'new-password',
    );
    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(http).toHaveBeenCalledWith(
      '/api/v1/admin/users/u1',
      expect.objectContaining({ method: 'PUT' }),
    );
    expect(http).toHaveBeenCalledWith('/api/v1/admin/users/u1/reset-password', {
      method: 'POST',
      body: JSON.stringify({ password: 'new-password' }),
    });
  });

  it('lists the user’s connected apps for revoking', async () => {
    renderAt('/users-groups/users/u1');
    expect(await screen.findByText('Claude')).toBeInTheDocument();
    expect(
      screen.getByText(
        'Only the user can connect an app. You can revoke theirs.',
      ),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: 'Revoke access' }),
    ).toBeInTheDocument();
  });

  it('shows not found for an unknown user', async () => {
    renderAt('/users-groups/users/nope');
    expect(await screen.findByText('User not found')).toBeInTheDocument();
  });
});
