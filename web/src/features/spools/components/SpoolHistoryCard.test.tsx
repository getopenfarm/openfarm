import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { SpoolHistoryCard } from './SpoolHistoryCard';

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function renderCard(response: Response) {
  const fetchMock = vi.fn((_input: RequestInfo | URL) =>
    Promise.resolve(response),
  );
  vi.stubGlobal('fetch', fetchMock);
  render(
    <QueryClientProvider
      client={
        new QueryClient({ defaultOptions: { queries: { retry: false } } })
      }
    >
      <MemoryRouter>
        <SpoolHistoryCard spoolId="spool-1" />
      </MemoryRouter>
    </QueryClientProvider>,
  );
  return fetchMock;
}

afterEach(() => vi.unstubAllGlobals());

describe('SpoolHistoryCard', () => {
  it('lists prints, corrections and manual usage with the weight left after each', async () => {
    const fetchMock = renderCard(
      jsonResponse({
        totalCount: 4,
        entries: [
          {
            id: 'e4',
            recordedAtUtc: '2026-10-22T10:00:00Z',
            changeGrams: -12.4,
            remainingAfterGrams: 787.6,
            source: 'Job',
            printId: 'print-1',
            printName: 'Benchy',
            printStatus: 'Failed',
          },
          {
            id: 'e3',
            recordedAtUtc: '2026-10-21T10:00:00Z',
            changeGrams: 30,
            remainingAfterGrams: 800,
            source: 'Correction',
            reason: 'weighed on scale',
          },
          {
            id: 'e2',
            recordedAtUtc: '2026-10-20T10:00:00Z',
            changeGrams: -5,
            remainingAfterGrams: 770,
            source: 'Manual',
          },
          {
            id: 'e1',
            recordedAtUtc: '2026-10-19T10:00:00Z',
            changeGrams: -20,
            remainingAfterGrams: null,
            source: 'Job',
            printId: null,
          },
        ],
      }),
    );

    const table = await screen.findByRole('table', { name: 'Spool history' });
    const rows = within(table).getAllByRole('row').slice(1);
    expect(rows).toHaveLength(4);

    const print = within(rows[0]);
    expect(print.getByRole('link', { name: 'Benchy' })).toHaveAttribute(
      'href',
      '/jobs/print-1',
    );
    expect(print.getByText('Failed')).toBeInTheDocument();
    expect(print.getByText('−12.4 g')).toBeInTheDocument();
    expect(print.getByText('787.6 g')).toBeInTheDocument();

    const correction = within(rows[1]);
    expect(correction.getByText('+30 g')).toBeInTheDocument();
    expect(correction.getByText('Weight correction')).toBeInTheDocument();
    expect(correction.getByText('weighed on scale')).toBeInTheDocument();

    expect(within(rows[2]).getByText('Manual usage')).toBeInTheDocument();

    // A print that is gone keeps its entry, without a link; a legacy entry has no weight afterwards.
    const legacy = within(rows[3]);
    expect(legacy.getByText('Print removed')).toBeInTheDocument();
    expect(legacy.queryByRole('link')).not.toBeInTheDocument();
    expect(legacy.getByText('−20 g')).toBeInTheDocument();
    expect(legacy.getAllByText('—').length).toBeGreaterThan(0);

    expect(String(fetchMock.mock.calls[0][0])).toContain(
      '/api/v1/spools/spool-1/history?pageSize=50',
    );
  });

  it('says nothing has been recorded when the history is empty', async () => {
    renderCard(jsonResponse({ entries: [], totalCount: 0 }));
    expect(
      await screen.findByText('No weight changes yet.'),
    ).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('states the total when there are more entries than shown', async () => {
    renderCard(
      jsonResponse({
        totalCount: 120,
        entries: [
          {
            id: 'e1',
            recordedAtUtc: '2026-10-22T10:00:00Z',
            changeGrams: -1,
            remainingAfterGrams: 99,
            source: 'Manual',
          },
        ],
      }),
    );
    expect(
      await screen.findByText('Showing latest 1 of 120.'),
    ).toBeInTheDocument();
  });

  it('offers a retry when the history cannot be loaded', async () => {
    renderCard(new Response(null, { status: 500 }));
    expect(
      await screen.findByText('Could not load history'),
    ).toBeInTheDocument();
  });
});
