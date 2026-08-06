import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import type { ReactNode } from 'react';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AssembliesPage } from './AssembliesPage';

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

  return render(<AssembliesPage />, { wrapper: Wrapper });
}

describe('AssembliesPage', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('shows an empty state before any assembly exists', async () => {
    stubFetch({ '/api/v1/assemblies': { items: [], totalCount: 0 } });

    renderPage();

    expect(await screen.findByText('No assemblies yet')).toBeInTheDocument();
  });

  it('renders assemblies fetched from the real API', async () => {
    stubFetch({
      '/api/v1/assemblies': {
        items: [
          { id: 'a1', name: 'Enclosure', description: null, lineCount: 3 },
        ],
        totalCount: 1,
      },
    });

    renderPage();

    expect(await screen.findByText('Enclosure')).toBeInTheDocument();
    expect(screen.getByText('3 lines')).toBeInTheDocument();
  });
});
