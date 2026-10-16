import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { createMemoryRouter, MemoryRouter, RouterProvider } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ConnectionSecretsPage } from './ConnectionSecretsPage';

function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });

  function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>{children}</MemoryRouter>
      </QueryClientProvider>
    );
  }

  return render(<ConnectionSecretsPage />, { wrapper: Wrapper });
}

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

describe('ConnectionSecretsPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('renders existing secrets in the table', async () => {
    const secret = {
      id: '11111111-1111-1111-1111-111111111111',
      printerSerialNumber: 'AC12345',
      maskedAccessCode: '***',
      createdAtUtc: '2026-01-01T00:00:00Z',
    };
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse([secret])));

    renderPage();

    expect(await screen.findByText('AC12345')).toBeInTheDocument();
    expect(screen.getByText('***')).toBeInTheDocument();
  });

  it('"Add credential" navigates to /settings/connection-secrets/new', async () => {
    const user = userEvent.setup();
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse([])));

    const queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
        mutations: { retry: false },
      },
    });
    const router = createMemoryRouter(
      [
        {
          path: '/settings/connection-secrets',
          element: <ConnectionSecretsPage />,
        },
        {
          path: '/settings/connection-secrets/new',
          element: <div>New credential page</div>,
        },
      ],
      { initialEntries: ['/settings/connection-secrets'] },
    );
    render(
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
      </QueryClientProvider>,
    );

    // Header action and empty-state action both read "Add credential".
    const [addButton] = await screen.findAllByRole('button', {
      name: 'Add credential',
    });
    await user.click(addButton);

    expect(await screen.findByText('New credential page')).toBeInTheDocument();
  });

  it('shows an error state with a retry button when the list request fails', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(jsonResponse({ title: 'Server error' }, 500));
    vi.stubGlobal('fetch', fetchMock);

    renderPage();

    expect(
      await screen.findByText(/Could not load connection credentials/),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
  });
});
