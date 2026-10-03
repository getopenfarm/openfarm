import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import type { ReactNode } from 'react';
import { MemoryRouter } from 'react-router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { QueuePage } from './QueuePage';

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

  return render(<QueuePage />, { wrapper: Wrapper });
}

describe('QueuePage', () => {
  // TanStack Virtual measures its scroll container's real layout (getBoundingClientRect,
  // offsetWidth/Height, and a ResizeObserver) — none of which jsdom provides, so without stubbing
  // all three the virtualizer computes a zero-size viewport and renders no rows at all.
  beforeEach(() => {
    vi.spyOn(Element.prototype, 'getBoundingClientRect').mockReturnValue({
      width: 800,
      height: 600,
      top: 0,
      left: 0,
      bottom: 600,
      right: 800,
      x: 0,
      y: 0,
      toJSON: () => {},
    });
    Object.defineProperty(HTMLElement.prototype, 'offsetHeight', {
      configurable: true,
      value: 600,
    });
    Object.defineProperty(HTMLElement.prototype, 'offsetWidth', {
      configurable: true,
      value: 800,
    });
    vi.stubGlobal(
      'ResizeObserver',
      class {
        observe() {}
        unobserve() {}
        disconnect() {}
      },
    );
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('shows an empty state before any job exists', async () => {
    stubFetch({ '/api/v1/print-jobs': { items: [], totalCount: 0 } });

    renderPage();

    expect(await screen.findByText('No jobs in the queue')).toBeInTheDocument();
  });

  it('renders queue jobs fetched from the real API', async () => {
    stubFetch({
      '/api/v1/print-jobs': {
        items: [
          {
            jobId: 'job-1',
            queuePosition: 1024,
            status: 'Queued',
            assignmentMode: 'Unassigned',
            pinnedPrinterId: null,
            dispatchedPrinterId: null,
            isBlocked: false,
            partId: 'part-1',
            partName: 'Bracket',
            plateQuantity: 4,
            runLineId: 'line-1',
            productionRunId: 'run-1',
            runDisplayLabel: 'Bracket ×20',
          },
        ],
        totalCount: 1,
      },
    });

    renderPage();

    expect(await screen.findByText('Bracket')).toBeInTheDocument();
    expect(screen.getByText('Bracket ×20')).toBeInTheDocument();
    expect(screen.getByText('Queued')).toBeInTheDocument();
  });
});
