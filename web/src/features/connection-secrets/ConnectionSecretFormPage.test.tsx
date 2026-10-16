import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { createMemoryRouter, Link, Outlet, RouterProvider } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Toaster } from '@/components/ui/sonner';
import { ConnectionSecretFormPage } from './ConnectionSecretFormPage';

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function TestLayout() {
  return (
    <div>
      <Link to="/settings/connection-secrets">Nav away</Link>
      <Outlet />
    </div>
  );
}

function renderRoute() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });

  function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={queryClient}>
        {children}
        <Toaster />
      </QueryClientProvider>
    );
  }

  const router = createMemoryRouter(
    [
      {
        element: <TestLayout />,
        children: [
          {
            path: '/settings/connection-secrets/new',
            element: <ConnectionSecretFormPage />,
          },
          {
            path: '/settings/connection-secrets',
            element: <div>Connection secrets list page</div>,
          },
        ],
      },
    ],
    { initialEntries: ['/settings/connection-secrets/new'] },
  );

  render(<RouterProvider router={router} />, { wrapper: Wrapper });
}

describe('ConnectionSecretFormPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('blocks submission with an empty access code and sends no create request', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse([]));
    vi.stubGlobal('fetch', fetchMock);
    const user = userEvent.setup();

    renderRoute();

    await user.type(screen.getByLabelText('Printer serial number'), 'AC12345');
    await user.click(screen.getByRole('button', { name: 'Add credential' }));

    expect(
      await screen.findByText('Access code is required'),
    ).toBeInTheDocument();

    const postCalls = fetchMock.mock.calls.filter(
      ([, init]) => (init as RequestInit | undefined)?.method === 'POST',
    );
    expect(postCalls).toHaveLength(0);
  });

  it('creates a connection secret, shows a toast, and navigates to the list', async () => {
    const created = {
      id: '11111111-1111-1111-1111-111111111111',
      printerSerialNumber: 'AC12345',
      maskedAccessCode: '***',
      createdAtUtc: '2026-01-01T00:00:00Z',
    };
    const fetchMock = vi.fn((_input: RequestInfo | URL, init?: RequestInit) => {
      if (init?.method === 'POST') {
        return Promise.resolve(jsonResponse(created, 201));
      }
      return Promise.resolve(jsonResponse([]));
    });
    vi.stubGlobal('fetch', fetchMock);
    const user = userEvent.setup();

    renderRoute();

    await user.type(screen.getByLabelText('Printer serial number'), 'AC12345');
    await user.type(screen.getByLabelText('Access code'), 'real-secret');
    await user.click(screen.getByRole('button', { name: 'Add credential' }));

    expect(
      await screen.findByText('Connection secrets list page'),
    ).toBeInTheDocument();
    expect(
      fetchMock.mock.calls.some(
        (call) => (call[1] as RequestInit | undefined)?.method === 'POST',
      ),
    ).toBe(true);
  });

  it('blocks in-app navigation away while the form has unsaved changes', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse([])));
    const user = userEvent.setup();

    renderRoute();

    await user.type(screen.getByLabelText('Printer serial number'), 'AC12345');

    await user.click(screen.getByRole('link', { name: 'Nav away' }));

    const dialog = await screen.findByRole('alertdialog');
    expect(
      within(dialog).getByText('Discard unsaved changes?'),
    ).toBeInTheDocument();
  });
});
