import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import type { ReactNode } from 'react';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { QueueJobRowResponse } from '@/lib/api/generated/endpoints.schemas';
import { QueueJobRow } from './QueueJobRow';

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function stubFetch(routes: Record<string, unknown>) {
  const fetchMock = vi.fn((input: RequestInfo | URL) => {
    const url = typeof input === 'string' ? input : input.toString();
    for (const [path, body] of Object.entries(routes)) {
      if (url.includes(path)) {
        return Promise.resolve(jsonResponse(body));
      }
    }
    return Promise.reject(new Error(`Unexpected fetch: ${url}`));
  });
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

function baseJob(
  overrides: Partial<QueueJobRowResponse> = {},
): QueueJobRowResponse {
  return {
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
    ...overrides,
  };
}

function renderRow(job: QueueJobRowResponse) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });

  function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <ul>{children}</ul>
        </MemoryRouter>
      </QueryClientProvider>
    );
  }

  return render(<QueueJobRow job={job} />, { wrapper: Wrapper });
}

describe('QueueJobRow', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('shows a Pin action for an Unassigned Queued job', async () => {
    stubFetch({ '/api/v1/printers': { printers: [] } });

    renderRow(baseJob());

    expect(
      await screen.findByRole('button', { name: /pin/i }),
    ).toBeInTheDocument();
  });

  it('shows an Unpin action for a Pinned Queued job', async () => {
    stubFetch({
      '/api/v1/printers': { printers: [{ id: 'printer-1', name: 'Prusa 1' }] },
    });

    renderRow(
      baseJob({ assignmentMode: 'Pinned', pinnedPrinterId: 'printer-1' }),
    );

    expect(
      await screen.findByRole('button', { name: /unpin/i }),
    ).toBeInTheDocument();
  });

  it('renders a blocked indicator when isBlocked is true', async () => {
    stubFetch({
      '/api/v1/printers': { printers: [{ id: 'printer-1', name: 'Prusa 1' }] },
    });

    renderRow(
      baseJob({
        assignmentMode: 'Pinned',
        pinnedPrinterId: 'printer-1',
        isBlocked: true,
      }),
    );

    expect(await screen.findByText(/blocked on/i)).toBeInTheDocument();
  });

  it('renders no pin/unpin action once the job has left the queue', () => {
    renderRow(baseJob({ status: 'Printing' }));

    expect(
      screen.queryByRole('button', { name: /pin/i }),
    ).not.toBeInTheDocument();
  });
});
