import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ProductionRunDetailResponse } from '@/lib/api/generated/endpoints.schemas';
import { ProductionRunDetailPage } from './ProductionRunDetailPage';

vi.mock('./hooks/useProductionHub', () => ({
  useProductionHub: () => {},
  invalidateProductionQueries: () => {},
}));

const RUN: ProductionRunDetailResponse = {
  id: 'run-1',
  displayLabel: 'Order #1042',
  name: 'Order #1042',
  status: 'Active',
  dueDate: null,
  notes: 'Ship Friday',
  createdAtUtc: '2026-10-27T08:00:00Z',
  lines: [
    {
      id: 'line-1',
      targetKind: 'Part',
      targetName: 'Bracket',
      quantity: 12,
      requirements: [
        {
          id: 'req-1',
          partId: 'part-1',
          partName: 'Bracket',
          sourcePartAvailable: true,
          quantity: 12,
          unitsPerPlate: 4,
          completedUnits: 4,
          writtenOffUnits: 0,
          plates: {
            completed: 1,
            printing: 0,
            queued: 1,
            needsAction: 1,
            writtenOff: 0,
          },
          estimatedMinutesPerPlate: 90,
          estimatedGramsPerPlate: 40,
          blockingReason: 'No printer has PETG loaded.',
          jobs: [
            { id: 'job-1', status: 'Completed', printerId: 'p1' },
            {
              id: 'job-2',
              status: 'Failed',
              printerId: 'p1',
              needsAction: true,
              canRequeue: true,
            },
            { id: 'job-3', status: 'Queued' },
          ],
        },
      ],
    },
  ],
};

function setup(run = RUN) {
  const posts: string[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = input.toString();
      if (init?.method === 'POST') {
        posts.push(url);
        return new Response(JSON.stringify({}), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        });
      }
      const body = url.includes('/api/v1/production-runs/run-1')
        ? run
        : url.endsWith('/api/v1/printers')
          ? { printers: [{ id: 'p1', name: 'P01' }] }
          : url.includes('/api/v1/parts/part-1')
            ? { id: 'part-1', thumbnails: [] }
            : {};
      return new Response(JSON.stringify(body), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    }),
  );
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    );
  }
  const router = createMemoryRouter(
    [{ path: '/production-runs/:id', element: <ProductionRunDetailPage /> }],
    { initialEntries: ['/production-runs/run-1'] },
  );
  render(<RouterProvider router={router} />, { wrapper: Wrapper });
  return posts;
}

describe('ProductionRunDetailPage', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('shows progress, what is left and every plate', async () => {
    setup();

    expect(
      await screen.findByRole('heading', { name: /Order #1042/ }),
    ).toBeInTheDocument();
    expect(screen.getByText('4/12')).toBeInTheDocument();
    expect(screen.getByText('1/3')).toBeInTheDocument();
    // One waiting plate of 90 min; the failed one is not counted until it is requeued.
    expect(screen.getByText('1h 30m')).toBeInTheDocument();
    expect(screen.getByText('40 g')).toBeInTheDocument();
    expect(
      screen.getByRole('img', {
        name: '1 of 3 plates done, 1 failed, 1 waiting',
      }),
    ).toBeInTheDocument();
    expect(screen.getByText('Ship Friday')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: /Bracket/ }));
    expect(screen.getByRole('link', { name: 'Plate 2' })).toHaveAttribute(
      'href',
      '/jobs/job-2',
    );
  });

  it('gathers lost plates and blocked parts, and writes off only after confirming', async () => {
    const posts = setup();
    const user = userEvent.setup();

    const card = (await screen.findByText('Needs attention')).closest(
      '[data-slot="card"]',
    );
    const attention = within(card as HTMLElement);
    expect(
      attention.getByText('No printer has PETG loaded.'),
    ).toBeInTheDocument();

    await user.click(attention.getByRole('button', { name: 'Write off' }));
    expect(posts).toHaveLength(0);
    await user.click(
      within(await screen.findByRole('alertdialog')).getByRole('button', {
        name: 'Write off',
      }),
    );
    await vi.waitFor(() =>
      expect(posts).toEqual([
        expect.stringContaining('/print-jobs/job-2/write-off'),
      ]),
    );

    await user.click(attention.getByRole('button', { name: 'Requeue' }));
    await vi.waitFor(() =>
      expect(posts.at(-1)).toContain('/print-jobs/job-2/requeue'),
    );
  });

  it('offers Close only once the run is complete', async () => {
    setup({ ...RUN, status: 'Complete' });
    expect(
      await screen.findByRole('button', { name: 'Close run' }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Stop backfill' }),
    ).not.toBeInTheDocument();
  });
});
