import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import type { ReactNode } from 'react';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { NonPrintedComponentsPage } from './NonPrintedComponentsPage';

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
  vi.stubGlobal(
    'fetch',
    vi.fn((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();
      for (const [path, body] of sortedRoutes) {
        if (url.includes(path)) {
          return Promise.resolve(jsonResponse(body));
        }
      }
      return Promise.reject(new Error(`Unexpected fetch: ${url}`));
    }),
  );
}

function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>{children}</MemoryRouter>
      </QueryClientProvider>
    );
  }

  return render(<NonPrintedComponentsPage />, { wrapper: Wrapper });
}

describe('NonPrintedComponentsPage', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('shows an empty state before any component exists', async () => {
    stubFetch({
      '/api/v1/non-printed-components': { items: [], totalCount: 0 },
    });

    renderPage();

    expect(
      await screen.findByText('No non-printed components'),
    ).toBeInTheDocument();
  });

  it('renders components fetched from the real API', async () => {
    stubFetch({
      '/api/v1/non-printed-components': {
        items: [{ id: 'c1', name: 'M3x8 screw', unitCost: 0.02 }],
        totalCount: 1,
      },
    });

    renderPage();

    expect(await screen.findByText('M3x8 screw')).toBeInTheDocument();
    expect(screen.getByText('USD 0.02')).toBeInTheDocument();
  });
});
