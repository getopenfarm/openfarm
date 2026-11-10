import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import type { ConsentResponse } from '@/lib/api/generated/endpoints.schemas';
import { httpClient } from '@/lib/api/httpClient';
import { ConsentPage } from './ConsentPage';

vi.mock('@/lib/api/httpClient', () => ({ httpClient: vi.fn() }));
const logout = vi.fn().mockResolvedValue(undefined);
vi.mock('@/lib/auth/useAuth', () => ({ useAuth: () => ({ logout }) }));
const http = vi.mocked(httpClient);

const scope = (key: string, extra: object = {}) => ({
  scope: key,
  label: `Label ${key}`,
  includes: [`Includes ${key}`],
  allowed: true,
  preselected: false,
  ...extra,
});
const consent = (extra: Partial<ConsentResponse> = {}): ConsentResponse => ({
  clientId: 'ofc_8c1d0b6e2f4a47c9a1e35d7b02c49f3a',
  clientName: 'Claude',
  returnsTo: { kind: 'Web', label: 'claude.ai' },
  unverifiedName: false,
  instance: 'https://farm.example.com',
  username: 'hao',
  accessEndsAtUtc: '2026-12-10T00:00:00Z',
  tiers: [
    {
      tier: 'Read',
      scopes: [
        scope('printers.read', { preselected: true }),
        scope('maintenance.read', { allowed: false }),
      ],
    },
    // A server that wrongly pre-selects these must still not tick them.
    { tier: 'Change', scopes: [scope('queue.write', { preselected: true })] },
    {
      tier: 'PrinterControl',
      scopes: [scope('printers.control', { preselected: true })],
    },
  ],
  ...extra,
});
const problem = (status: number, body: object) =>
  new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/problem+json' },
  });

function Where() {
  const location = useLocation();
  return <p>at {location.pathname + location.search}</p>;
}
function setup(data: ConsentResponse | Response = consent()) {
  http.mockImplementation(async (_url, options) => {
    if (options?.method === 'POST')
      return { data: { redirectUri: 'https://claude.ai/cb?code=abc' } };
    if (data instanceof Response) throw data;
    return { data };
  });
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={['/connect?request=req1']}>
        <Routes>
          <Route path="/connect" element={<ConsentPage />} />
          <Route path="/login" element={<Where />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}
const posts = () => http.mock.calls.filter(([, o]) => o?.method === 'POST');

const originalLocation = window.location;
let assign: ReturnType<typeof vi.fn>;
beforeEach(() => {
  http.mockReset();
  logout.mockClear();
  assign = vi.fn();
  Object.defineProperty(window, 'location', {
    configurable: true,
    value: { ...originalLocation, assign },
  });
});
afterEach(() => {
  Object.defineProperty(window, 'location', {
    configurable: true,
    value: originalLocation,
  });
});

it('pre-ticks allowed reads only, and explains disabled scopes', async () => {
  document.title = 'OpenFarm';
  const view = setup(consent({ unverifiedName: true }));
  expect(
    await screen.findByRole('heading', { name: 'Connect Claude' }),
  ).toBeInTheDocument();
  expect(document.title).toBe('Connect app - OpenFarm');
  expect(screen.getByLabelText('Label printers.read')).toBeChecked();
  expect(screen.getByLabelText('Label queue.write')).not.toBeChecked();
  expect(screen.getByLabelText('Label printers.control')).not.toBeChecked();
  expect(screen.getByLabelText('Label maintenance.read')).toBeDisabled();
  expect(
    screen.getByText('Your account does not have this permission.'),
  ).toBeInTheDocument();
  expect(
    screen.getByText('ofc_8c1d0b6e2f4a47c9a1e35d7b02c49f3a'),
  ).toBeInTheDocument();
  expect(screen.getByText('Returns to claude.ai.')).toBeInTheDocument();
  expect(screen.getByText(/The app name is not verified/)).toHaveTextContent(
    'The app name is not verified. Allow only if you started this from claude.ai.',
  );
  expect(screen.getByText(/The app can pause and stop/)).toHaveClass(
    'text-destructive',
  );
  view.unmount();
  expect(document.title).toBe('OpenFarm');
});

it('disables Allow only when no scope is allowed', async () => {
  setup(
    consent({
      tiers: [
        { tier: 'Read', scopes: [scope('printers.read', { allowed: false })] },
      ],
    }),
  );
  expect(
    await screen.findByRole('button', { name: 'Allow selected access' }),
  ).toBeDisabled();
});

it('asks for a permission before sending an empty approval', async () => {
  const user = userEvent.setup();
  setup();
  await user.click(await screen.findByLabelText('Label printers.read'));
  const allow = screen.getByRole('button', { name: 'Allow selected access' });
  expect(allow).toBeEnabled();
  await user.click(allow);
  expect(screen.getByRole('alert')).toHaveTextContent(
    'Select at least one available permission.',
  );
  expect(posts()).toHaveLength(0);
});

it('approves the ticked scopes and follows the server redirect', async () => {
  const user = userEvent.setup();
  setup();
  await user.click(await screen.findByLabelText('Label queue.write'));
  await user.click(
    screen.getByRole('button', { name: 'Allow selected access' }),
  );
  await waitFor(() =>
    expect(assign).toHaveBeenCalledWith('https://claude.ai/cb?code=abc'),
  );
  expect(JSON.parse(posts()[0][1]?.body as string)).toEqual({
    request: 'req1',
    approve: true,
    scopes: ['printers.read', 'queue.write'],
  });
  expect(screen.getByRole('status')).toHaveTextContent(
    'Returning to claude.ai…',
  );
});

it('denies with Cancel and follows the server redirect', async () => {
  const user = userEvent.setup();
  setup();
  await user.click(await screen.findByRole('button', { name: 'Cancel' }));
  await waitFor(() =>
    expect(assign).toHaveBeenCalledWith('https://claude.ai/cb?code=abc'),
  );
  expect(JSON.parse(posts()[0][1]?.body as string)).toMatchObject({
    approve: false,
  });
});

it('shows a server scope rejection inline', async () => {
  const user = userEvent.setup();
  setup();
  await screen.findByLabelText('Label printers.read');
  http.mockRejectedValueOnce(problem(400, { code: 'invalid_scope' }));
  await user.click(
    screen.getByRole('button', { name: 'Allow selected access' }),
  );
  expect(await screen.findByRole('alert')).toHaveTextContent(
    'Select at least one available permission.',
  );
  expect(assign).not.toHaveBeenCalled();
});

it('shows an expired request without a retry', async () => {
  setup(
    problem(400, {
      code: 'invalid_request',
      title: 'Connection request expired',
    }),
  );
  expect(
    await screen.findByText('Connection request expired'),
  ).toBeInTheDocument();
  expect(
    screen.getByText('Start again from your AI assistant.'),
  ).toBeInTheDocument();
  expect(screen.queryByRole('button')).not.toBeInTheDocument();
});

it('signs out and comes back to this request after sign-in', async () => {
  const user = userEvent.setup();
  setup();
  await user.click(await screen.findByRole('button', { name: 'Sign out' }));
  expect(logout).toHaveBeenCalled();
  expect(
    await screen.findByText('at /login?returnTo=%2Fconnect%3Frequest%3Dreq1'),
  ).toBeInTheDocument();
});
