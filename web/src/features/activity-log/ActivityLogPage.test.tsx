import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import type { ReactNode } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ActivityLogPage } from './ActivityLogPage';

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    );
  }

  return render(<ActivityLogPage />, { wrapper: Wrapper });
}

describe('ActivityLogPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('renders entries and pagination controls', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        jsonResponse({
          items: [
            {
              id: '11111111-1111-1111-1111-111111111111',
              entityType: 'ConnectionSecret',
              entityId: '22222222-2222-2222-2222-222222222222',
              eventType: 'Created',
              actorId: '33333333-3333-3333-3333-333333333333',
              actorType: 'Operator',
              actorDisplayName: 'admin@openfarm.local',
              timestampUtc: '2026-01-01T00:00:00Z',
            },
          ],
          page: 1,
          pageSize: 25,
          totalCount: 1,
        }),
      ),
    );

    renderPage();

    expect(
      await screen.findByText('Connection secret created'),
    ).toBeInTheDocument();
    expect(screen.getByText(/admin@openfarm\.local/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Previous' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Next' })).toBeDisabled();
    expect(screen.getByText('Page 1 · 1 entries')).toBeInTheDocument();
  });

  it('shows an error state with a retry button when the request fails', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(jsonResponse({ title: 'Server error' }, 500)),
    );

    renderPage();

    expect(
      await screen.findByText(/Couldn't load the activity log/),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
  });
});
