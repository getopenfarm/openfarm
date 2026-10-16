import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import type { ReactNode } from 'react';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ProductionRunsPage } from './ProductionRunsPage';

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function stubFetch(routes: Record<string, unknown>) {
  const sortedRoutes = Object.entries(routes).sort(
    ([a], [b]) => b.length - a.length,
  );
  const fetchMock = vi.fn((input: RequestInfo | URL) => {
    const url = typeof input === 'string' ? input : input.toString();
    for (const [path, body] of sortedRoutes) {
      if (url.includes(path)) {
        return Promise.resolve(jsonResponse(body));
      }
    }
    return Promise.reject(new Error(`Unexpected fetch: ${url}`));
  });
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

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

  return render(<ProductionRunsPage />, { wrapper: Wrapper });
}

describe('ProductionRunsPage', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('shows an empty state before any run exists', async () => {
    stubFetch({ '/api/v1/production-runs': { items: [], totalCount: 0 } });

    renderPage();

    expect(await screen.findByText('No production runs')).toBeInTheDocument();
  });

  it('renders production runs fetched from the real API', async () => {
    stubFetch({
      '/api/v1/production-runs': {
        items: [
          {
            id: 'run-1',
            displayLabel: 'Bracket ×20',
            status: 'Active',
            dueDate: null,
            lineCount: 1,
            jobSummary: {
              queued: 3,
              printing: 1,
              paused: 0,
              completed: 1,
              failed: 0,
              cancelled: 0,
            },
          },
        ],
        totalCount: 1,
      },
    });

    renderPage();

    expect(await screen.findByText('Bracket ×20')).toBeInTheDocument();
    expect(screen.getByText('Active')).toBeInTheDocument();
  });
});
