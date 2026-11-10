import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { httpClient } from '@/lib/api/httpClient';
import { ProfilePage } from './ProfilePage';

vi.mock('@/lib/api/httpClient', () => ({ httpClient: vi.fn() }));
vi.mock('@/lib/auth/permissions', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/lib/auth/permissions')>()),
  useIdentity: () => ({
    data: {
      userId: 'me',
      displayName: 'Mei',
      email: 'mei@example.com',
      username: 'mei',
    },
    isError: false,
  }),
}));
const http = vi.mocked(httpClient);

function setup(tab = 'profile') {
  return render(
    <QueryClientProvider
      client={
        new QueryClient({
          defaultOptions: {
            queries: { retry: false },
            mutations: { retry: false },
          },
        })
      }
    >
      <MemoryRouter initialEntries={[`/profile?tab=${tab}`]}>
        <ProfilePage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  http.mockReset();
});

describe('Profile form feedback', () => {
  it('keeps the section save workflow and disables conflicting controls while saving', async () => {
    let finish!: () => void;
    http.mockImplementation(
      () =>
        new Promise<void>((resolve) => {
          finish = resolve;
        }),
    );
    setup();
    const user = userEvent.setup();
    await user.clear(screen.getByLabelText('Display name'));
    await user.type(screen.getByLabelText('Display name'), 'Mei Ling');
    await user.click(screen.getByRole('button', { name: 'Save' }));
    expect(screen.getByRole('button', { name: 'Saving…' })).toBeDisabled();
    expect(screen.getByLabelText('Display name')).toBeDisabled();
    expect(http).toHaveBeenCalledExactlyOnceWith('/api/v1/auth/profile', {
      method: 'PUT',
      body: JSON.stringify({
        name: 'Mei Ling',
        email: 'mei@example.com',
        username: 'mei',
      }),
    });
    await act(async () => finish());
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled(),
    );
    expect(screen.getByLabelText('Display name')).toHaveValue('Mei Ling');
  });

  it('associates password mismatch feedback and preserves values after request failure', async () => {
    http.mockRejectedValue(new Error('Unavailable'));
    setup('security');
    const user = userEvent.setup();
    await user.type(screen.getByLabelText('Current password'), 'old-password');
    await user.type(screen.getByLabelText('New password'), 'new-password');
    const confirm = screen.getByLabelText('Confirm new password');
    await user.type(confirm, 'different');
    expect(confirm).toHaveAccessibleDescription('Passwords do not match.');
    expect(confirm).toHaveAttribute('aria-invalid', 'true');
    expect(
      screen.getByRole('button', { name: 'Change password' }),
    ).toBeDisabled();
    await user.clear(confirm);
    await user.type(confirm, 'new-password');
    await user.click(screen.getByRole('button', { name: 'Change password' }));
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Could not change password.',
    );
    expect(screen.getByLabelText('New password')).toHaveValue('new-password');
  });

  it('keeps session confirmation pending, reports failure, and returns focus after cancel', async () => {
    let reject!: (error: Error) => void;
    http.mockImplementation((_url, init) =>
      init?.method === 'DELETE'
        ? new Promise<void>((_resolve, rejectRequest) => {
            reject = rejectRequest;
          })
        : Promise.resolve({
            data: [{ id: 'other', device: 'Other browser', isCurrent: false }],
          }),
    );
    setup('sessions');
    const user = userEvent.setup();
    const revoke = await screen.findByRole('button', { name: 'Revoke' });
    await user.click(revoke);
    const dialog = screen.getByRole('alertdialog');
    await user.click(within(dialog).getByRole('button', { name: 'Revoke' }));
    expect(
      within(dialog).getByRole('button', { name: 'Revoke' }),
    ).toBeDisabled();
    await user.keyboard('{Escape}');
    expect(screen.getByRole('alertdialog')).toBeInTheDocument();
    await act(async () => reject(new Error('Unavailable')));
    expect(await within(dialog).findByRole('alert')).toHaveTextContent(
      'Could not revoke session.',
    );
    await user.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    await waitFor(() => expect(revoke).toHaveFocus());
    expect(
      http.mock.calls.filter(([, init]) => init?.method === 'DELETE'),
    ).toHaveLength(1);
  });
});

describe('Connected apps', () => {
  it('shows the MCP server URL from the server and points tokens there', async () => {
    http.mockImplementation((async (url: string) =>
      url === '/api/v1/auth/apps'
        ? {
            data: {
              mcpUrl: 'https://farm.example.com/mcp',
              signInEnabled: false,
            },
            status: 200,
            headers: new Headers(),
          }
        : { data: [], status: 200, headers: new Headers() }) as never);
    setup('apps');
    expect(
      await screen.findByText('https://farm.example.com/mcp'),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('heading', { name: 'Connected apps' }),
    ).toBeInTheDocument();
  });

  it('collapses the connect card once an app is connected', async () => {
    http.mockImplementation((async () => ({
      data: {
        mcpUrl: 'https://farm.example.com/mcp',
        signInEnabled: true,
        apps: [{ id: 'a1', clientName: 'Claude', access: [] }],
      },
      status: 200,
      headers: new Headers(),
    })) as never);
    setup('apps');
    expect(await screen.findByText('Claude')).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: 'Connect an AI assistant' }),
    ).toHaveAttribute('aria-expanded', 'false');
    expect(
      screen.queryByText('https://farm.example.com/mcp'),
    ).not.toBeInTheDocument();
  });
});
