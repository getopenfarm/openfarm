import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { beforeEach, expect, it, vi } from 'vitest';
import { httpClient } from '@/lib/api/httpClient';
import { defaultTokenExpiry, TokenCreatePage } from './TokenCreatePage';

vi.mock('@/lib/api/httpClient', () => ({ httpClient: vi.fn() }));
const http = vi.mocked(httpClient);
const credential = 'ofp_test-credential';
const options = {
  areas: [
    {
      key: 'printers',
      label: 'Printers',
      noun: 'printers',
      administratorOnly: false,
    },
  ],
  permissions: [
    {
      key: 'printers:list',
      area: 'printers',
      label: 'List',
      description: 'See printers.',
      isRead: true,
      isSensitive: false,
      isDestructive: false,
      isExplicit: false,
      verb: 'list',
    },
  ],
  available: ['printers:list'],
};
function setup() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  const router = createMemoryRouter(
    [
      { path: '/profile/tokens/new', element: <TokenCreatePage /> },
      { path: '/profile', element: <div>Token list</div> },
    ],
    { initialEntries: ['/profile/tokens/new'] },
  );
  render(
    <QueryClientProvider client={client}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );
  return { client, router };
}
beforeEach(() => {
  http.mockReset();
  http.mockImplementation(async (url) => ({
    data: url.endsWith('/options')
      ? options
      : { credential, token: { id: 'one' } },
  }));
});
it('creates a scoped token, reveals once, copies it and keeps it out of query caches', async () => {
  const user = userEvent.setup();
  const { client } = setup();
  await user.type(await screen.findByLabelText('Name'), 'Inventory');
  await user.click(screen.getByRole('radio', { name: 'View' }));
  await user.click(screen.getByRole('button', { name: 'Create token' }));
  expect(await screen.findByText(credential)).toBeInTheDocument();
  expect(http).toHaveBeenCalledWith(
    '/api/v1/auth/tokens',
    expect.objectContaining({
      method: 'POST',
      body: expect.stringContaining('"permissions":["printers:list"]'),
    }),
  );
  await user.click(screen.getByRole('button', { name: 'Copy' }));
  expect(await navigator.clipboard.readText()).toBe(credential);
  expect(screen.getByRole('button', { name: 'Copied' })).toBeInTheDocument();
  expect(
    JSON.stringify(
      client
        .getQueryCache()
        .getAll()
        .map((q) => q.state.data),
    ),
  ).not.toContain(credential);
  expect(client.getMutationCache().getAll()).toHaveLength(0);
  await user.click(screen.getByRole('button', { name: 'Done' }));
  expect(await screen.findByText('Token list')).toBeInTheDocument();
  expect(screen.queryByText(credential)).not.toBeInTheDocument();
});
it('validates the scope and preserves the form after a failed create', async () => {
  const user = userEvent.setup();
  setup();
  await user.type(await screen.findByLabelText('Name'), 'Inventory');
  await user.click(screen.getByRole('button', { name: 'Create token' }));
  expect(
    await screen.findByText('Choose at least one permission.'),
  ).toBeInTheDocument();
  await user.click(screen.getByRole('radio', { name: 'View' }));
  http.mockImplementation(async (url) => {
    if (url.endsWith('/options')) return { data: options };
    throw new Error('offline');
  });
  await user.click(screen.getByRole('button', { name: 'Create token' }));
  expect(
    await screen.findByText('Could not create token.'),
  ).toBeInTheDocument();
  expect(screen.getByLabelText('Name')).toHaveValue('Inventory');
});
it('clamps the six-month default to the last day of the destination month', () => {
  expect(defaultTokenExpiry(new Date(2026, 7, 31))).toBe('2027-02-28');
  expect(defaultTokenExpiry(new Date(2026, 10, 3))).toBe('2027-05-03');
});
it('reports clipboard failure with a manual-copy fallback', async () => {
  const user = userEvent.setup();
  setup();
  await user.type(await screen.findByLabelText('Name'), 'Inventory');
  await user.click(screen.getByRole('radio', { name: 'View' }));
  await user.click(screen.getByRole('button', { name: 'Create token' }));
  await screen.findByText(credential);
  vi.spyOn(navigator.clipboard, 'writeText').mockRejectedValueOnce(
    new Error('denied'),
  );
  await user.click(screen.getByRole('button', { name: 'Copy' }));
  expect(await screen.findByRole('alert')).toHaveTextContent(
    'Select the credential and copy it manually.',
  );
});

it('keeps the access summary in its own side card and updates it with selected abilities', async () => {
  const user = userEvent.setup();
  setup();
  const rail = await screen.findByRole('complementary', {
    name: 'Token access summary',
  });
  expect(
    within(rail).getByRole('region', { name: 'This token can' }),
  ).toBeInTheDocument();
  expect(rail).toHaveTextContent(
    'Nothing yet. Pick a level for at least one area.',
  );
  await user.click(screen.getByRole('radio', { name: 'View' }));
  expect(rail).toHaveTextContent('Printers');
  expect(rail).toHaveTextContent('Can list printers.');
  expect(rail).not.toHaveTextContent('Nothing yet.');
});
it('clears expiry to no expiry and Discard restores the original form', async () => {
  const user = userEvent.setup();
  setup();
  const trigger = await screen.findByLabelText('Expires');
  const original = trigger.textContent;
  await user.click(
    screen.getByRole('button', { name: 'Remove the expiry date' }),
  );
  expect(trigger).toHaveTextContent('Never expires');
  expect(
    screen.getByText(
      'Anyone who gets this token can use it until you revoke it.',
    ),
  ).toBeInTheDocument();
  await user.click(screen.getByRole('button', { name: 'Discard' }));
  expect(trigger.textContent).toBe(original);
  await user.click(
    screen.getByRole('button', { name: 'Remove the expiry date' }),
  );
  await user.type(screen.getByLabelText('Name'), 'No expiry');
  await user.click(screen.getByRole('radio', { name: 'View' }));
  await user.click(screen.getByRole('button', { name: 'Create token' }));
  await screen.findByText(credential);
  expect(http).toHaveBeenCalledWith(
    '/api/v1/auth/tokens',
    expect.objectContaining({
      body: expect.stringContaining('"expiresOn":null'),
    }),
  );
  expect(
    screen.getByRole('complementary', { name: 'Token access summary' }),
  ).toHaveTextContent('Can list printers.');
});
