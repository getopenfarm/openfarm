import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { MemoryRouter } from 'react-router';
import { toast } from 'sonner';
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

  return {
    ...render(<QueueJobRow job={job} />, { wrapper: Wrapper }),
    queryClient,
  };
}

describe('QueueJobRow', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

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

  it.each([true, false])(
    'opens diagnosis from the matching field (blocked=%s)',
    async (isBlocked) => {
      const user = userEvent.setup();
      const requests = stubFetch({
        '/api/v1/printers': { printers: [] },
        '/matching-diagnostics': {
          outcome: isBlocked ? 'Blocked' : 'Eligible',
          summary: 'Matching checked.',
        },
      });
      renderRow(baseJob({ isBlocked }));
      const label = isBlocked
        ? 'No matching printer'
        : 'Matching printer available';
      expect(
        screen.queryByText('Why isn’t this printing?'),
      ).not.toBeInTheDocument();
      expect(
        requests.mock.calls.some(([url]) =>
          url.toString().includes('/matching-diagnostics'),
        ),
      ).toBe(false);
      await user.click(screen.getByRole('button', { name: label }));
      expect(await screen.findByText('Matching checked.')).toBeInTheDocument();
      expect(
        screen.getByRole('dialog', { name: 'Job diagnosis' }),
      ).toBeInTheDocument();
    },
  );

  it('removes a queued job only after confirmation', async () => {
    const user = userEvent.setup();
    const requests = stubFetch({
      '/api/v1/printers': { printers: [] },
      '/api/v1/print-jobs/job-1/cancel': { id: 'job-1', status: 'Cancelled' },
    });
    const { queryClient } = renderRow(baseJob());
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    await user.click(screen.getByRole('button', { name: 'Remove job' }));
    const confirmation = screen.getByRole('alertdialog');
    expect(
      requests.mock.calls.some(([url]) => url.toString().includes('/cancel')),
    ).toBe(false);
    await user.click(
      within(confirmation).getByRole('button', { name: 'Cancel' }),
    );
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Remove job' }));
    await user.click(
      within(screen.getByRole('alertdialog')).getByRole('button', {
        name: 'Remove job',
      }),
    );
    await waitFor(() =>
      expect(requests).toHaveBeenCalledWith(
        expect.stringContaining('/api/v1/print-jobs/job-1/cancel'),
        expect.objectContaining({ method: 'POST' }),
      ),
    );
    await waitFor(() => expect(invalidate).toHaveBeenCalled());
  });

  it('keeps a job actionable and shows the server reason when removal fails', async () => {
    const errorToast = vi.spyOn(toast, 'error');
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) =>
        input.toString().includes('/cancel')
          ? jsonResponse({ detail: 'This job has already finished.' }, 409)
          : jsonResponse({ printers: [] }),
      ),
    );
    const { queryClient } = renderRow(baseJob());
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: 'Remove job' }));
    await user.click(
      within(screen.getByRole('alertdialog')).getByRole('button', {
        name: 'Remove job',
      }),
    );
    await waitFor(() =>
      expect(errorToast).toHaveBeenCalledWith('This job has already finished.'),
    );
    expect(screen.getByRole('button', { name: 'Remove job' })).toBeEnabled();
    expect(invalidate).not.toHaveBeenCalled();
  });

  it.each([
    { status: 'Printing' },
    { status: 'Paused' },
    { status: 'Completed' },
    { dispatchedPrinterId: 'printer-1' },
  ])(
    'does not offer queue removal for an active or finished job: %j',
    (overrides) => {
      stubFetch({ '/api/v1/printers': { printers: [] } });
      renderRow(baseJob(overrides));
      expect(
        screen.queryByRole('button', { name: 'Remove job' }),
      ).not.toBeInTheDocument();
      expect(
        screen.getByRole('button', { name: 'Job diagnosis' }),
      ).toBeInTheDocument();
    },
  );
});
