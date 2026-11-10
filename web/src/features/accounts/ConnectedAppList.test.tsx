import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router';
import { beforeEach, expect, it, vi } from 'vitest';
import { httpClient } from '@/lib/api/httpClient';
import { ConnectedAppList } from './ConnectedAppList';

vi.mock('@/lib/api/httpClient', () => ({ httpClient: vi.fn() }));
vi.mock('sonner', () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
const http = vi.mocked(httpClient);
const { toast } = await import('sonner');

const app = {
  id: 'app1',
  clientName: 'Claude',
  clientIdSuffix: '9f2a',
  returnsTo: { kind: 'Web', label: 'claude.ai' },
  access: [
    {
      area: 'Printers',
      level: 'Read',
      extras: [],
      title: 'Can see printers.',
      control: false,
    },
    {
      area: 'Queue',
      level: 'Change',
      extras: ['Pause', 'Cancel'],
      title: 'Can change the queue and stop prints.',
      control: true,
    },
  ],
  createdAtUtc: '2026-11-10T00:00:00Z',
  expiresAtUtc: '2026-12-10T00:00:00Z',
  lastUsedAtUtc: null,
};
function setup(apps: object[], userId?: string) {
  http.mockImplementation(async (url) => ({
    data: url.endsWith('/apps')
      ? userId
        ? apps
        : { mcpUrl: null, signInEnabled: true, apps }
      : undefined,
  }));
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <ConnectedAppList userId={userId} />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}
beforeEach(() => {
  http.mockReset();
});

it('lists an app with its access chips and dates', async () => {
  setup([app]);
  expect(await screen.findByText('Claude')).toBeInTheDocument();
  expect(
    screen.getByText('Returns to claude.ai · Client ID ofc_…9f2a'),
  ).toBeInTheDocument();
  const control = screen.getByTitle('Can change the queue and stop prints.');
  expect(control).toHaveTextContent('QueueChange + Pause, Cancel');
  expect(control).toHaveClass('text-kind-amber');
  expect(screen.getByTitle('Can see printers.')).toHaveClass('text-kind-zinc');
  expect(screen.getByText('Expires (UTC)')).toBeInTheDocument();
  expect(screen.getByText('Never')).toBeInTheDocument();
});

it('confirms, revokes through the session endpoint and returns focus', async () => {
  const user = userEvent.setup();
  setup([app]);
  const button = await screen.findByRole('button', { name: 'Revoke access' });
  await user.click(button);
  const dialog = screen.getByRole('alertdialog');
  expect(dialog).toHaveTextContent('Revoke access for “Claude”?');
  expect(dialog).toHaveTextContent(
    'The app is disconnected at once. Connect it again from the assistant to restore access.',
  );
  await user.click(
    within(dialog).getByRole('button', { name: 'Revoke access' }),
  );
  await waitFor(() =>
    expect(http).toHaveBeenCalledWith('/api/v1/auth/sessions/app1', {
      method: 'DELETE',
    }),
  );
  await waitFor(() =>
    expect(toast.success).toHaveBeenCalledWith('App access revoked.'),
  );
  await waitFor(() => expect(button).toHaveFocus());
});

it('points token users to Access tokens when empty', async () => {
  setup([]);
  expect(await screen.findByText('No connected apps')).toBeInTheDocument();
  expect(screen.getByRole('link', { name: 'Access tokens' })).toHaveAttribute(
    'href',
    '/profile?tab=tokens',
  );
});

it('serves the admin section from the user’s apps', async () => {
  const user = userEvent.setup();
  setup([app], 'u1');
  await user.click(
    await screen.findByRole('button', { name: 'Revoke access' }),
  );
  await user.click(
    within(screen.getByRole('alertdialog')).getByRole('button', {
      name: 'Revoke access',
    }),
  );
  expect(http).toHaveBeenCalledWith('/api/v1/admin/users/u1/apps');
  await waitFor(() =>
    expect(http).toHaveBeenCalledWith('/api/v1/admin/users/u1/sessions/app1', {
      method: 'DELETE',
    }),
  );
});

it('tells an administrator the user has none', async () => {
  setup([], 'u1');
  expect(
    await screen.findByText('This user has no connected apps.'),
  ).toBeInTheDocument();
});
