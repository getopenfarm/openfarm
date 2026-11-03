import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router';
import { beforeEach, expect, it, vi } from 'vitest';
import { httpClient } from '@/lib/api/httpClient';
import { TokenList } from './TokenList';

vi.mock('@/lib/api/httpClient', () => ({ httpClient: vi.fn() }));
const http = vi.mocked(httpClient);
const rows = [
  {
    id: 'one',
    name: 'Inventory',
    lastFour: '1234',
    permissions: ['printers:list'],
    expiresAtUtc: '2099-01-01T23:59:59Z',
    lastUsedAtUtc: null,
  },
];
function setup(userId?: string) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <TokenList userId={userId} />
      </MemoryRouter>
    </QueryClientProvider>,
  );
  return client;
}
beforeEach(() => {
  http.mockReset();
  http.mockImplementation(async (url) => ({
    data: url.endsWith('/options') ? { areas: [], permissions: [] } : rows,
  }));
});
it('confirms regeneration and keeps the replacement out of query caches', async () => {
  const user = userEvent.setup();
  const client = setup();
  await user.click(await screen.findByRole('button', { name: 'Regenerate' }));
  const dialog = screen.getByRole('alertdialog');
  expect(dialog).toHaveTextContent('The old credential stops working at once.');
  http.mockImplementation(async (url, options) => ({
    data:
      options?.method === 'POST'
        ? { credential: 'ofp_replacement', token: rows[0] }
        : url.endsWith('/options')
          ? { areas: [], permissions: [] }
          : rows,
  }));
  await user.click(within(dialog).getByRole('button', { name: 'Regenerate' }));
  expect(await screen.findByText('ofp_replacement')).toBeInTheDocument();
  expect(
    JSON.stringify(
      client
        .getQueryCache()
        .getAll()
        .map((q) => q.state.data),
    ),
  ).not.toContain('ofp_replacement');
  await user.click(screen.getByRole('button', { name: 'Done' }));
  expect(screen.queryByText('ofp_replacement')).not.toBeInTheDocument();
});
it('keeps failed revocation open and restores keyboard focus on cancel', async () => {
  const user = userEvent.setup();
  setup();
  const button = await screen.findByRole('button', { name: 'Revoke' });
  button.focus();
  await user.keyboard('{Enter}');
  const dialog = screen.getByRole('alertdialog');
  http.mockRejectedValueOnce(new Error('offline'));
  await user.click(within(dialog).getByRole('button', { name: 'Revoke' }));
  expect(await screen.findByRole('alert')).toHaveTextContent(
    'Could not change this token.',
  );
  await user.click(within(dialog).getByRole('button', { name: 'Cancel' }));
  await waitFor(() => expect(button).toHaveFocus());
});
it('lets administrators revoke without creating or regenerating another user’s credential', async () => {
  const user = userEvent.setup();
  setup('other');
  await screen.findByText('Inventory');
  expect(
    screen.queryByRole('link', { name: 'Add token' }),
  ).not.toBeInTheDocument();
  expect(
    screen.queryByRole('button', { name: 'Regenerate' }),
  ).not.toBeInTheDocument();
  await user.click(screen.getByRole('button', { name: 'Revoke' }));
  await user.click(
    within(screen.getByRole('alertdialog')).getByRole('button', {
      name: 'Revoke',
    }),
  );
  await waitFor(() =>
    expect(http).toHaveBeenCalledWith('/api/v1/admin/users/other/tokens/one', {
      method: 'DELETE',
    }),
  );
});
