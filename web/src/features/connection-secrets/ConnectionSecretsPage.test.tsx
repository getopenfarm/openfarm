import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Toaster } from '@/components/ui/sonner';
import { ConnectionSecretsPage } from './ConnectionSecretsPage';

function renderPage() {
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

  it('shows the masked value in the table after a successful create', async () => {
    const created = {
      id: '11111111-1111-1111-1111-111111111111',
      printerSerialNumber: 'AC12345',
      maskedAccessCode: '***',
      createdAtUtc: '2026-01-01T00:00:00Z',
    };
    let hasCreated = false;

    const fetchMock = vi.fn((_input: RequestInfo | URL, init?: RequestInit) => {
      if (init?.method === 'POST') {
        hasCreated = true;
        return Promise.resolve(jsonResponse(created, 201));
      }
      return Promise.resolve(jsonResponse(hasCreated ? [created] : []));
    });
    vi.stubGlobal('fetch', fetchMock);

    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getByRole('button', { name: 'Add credential' }));
    await user.type(screen.getByLabelText('Printer serial number'), 'AC12345');
    await user.type(screen.getByLabelText('Access code'), 'real-secret');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(screen.getByText('***')).toBeInTheDocument());
    expect(screen.getByText('AC12345')).toBeInTheDocument();
  });

  it('blocks submission with an empty access code and sends no create request', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse([]));
    vi.stubGlobal('fetch', fetchMock);

    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getByRole('button', { name: 'Add credential' }));
    await user.type(screen.getByLabelText('Printer serial number'), 'AC12345');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(
      await screen.findByText('Access code is required'),
    ).toBeInTheDocument();

    const postCalls = fetchMock.mock.calls.filter(
      ([, init]) => (init as RequestInit | undefined)?.method === 'POST',
    );
    expect(postCalls).toHaveLength(0);
  });

  it('shows an error state with a retry button when the list request fails', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(jsonResponse({ title: 'Server error' }, 500));
    vi.stubGlobal('fetch', fetchMock);

    renderPage();

    expect(
      await screen.findByText(/Couldn't load connection secrets/),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
  });
});
