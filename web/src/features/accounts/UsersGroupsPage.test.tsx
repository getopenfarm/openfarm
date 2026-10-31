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

  it('shows not found for an unknown user', async () => {
    renderAt('/users-groups/users/nope');
    expect(await screen.findByText('User not found')).toBeInTheDocument();
  });
});
