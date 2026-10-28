import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ProductionRunSummaryResponse } from '@/lib/api/generated/endpoints.schemas';
import { ProductionRunsPage } from './ProductionRunsPage';

vi.mock('./hooks/useProductionHub', () => ({
  useProductionHub: () => {},
  invalidateProductionQueries: () => {},
}));

const order: ProductionRunSummaryResponse = {
  id: 'run-1',
  displayLabel: 'Order #1042',
  name: 'Order #1042',
  status: 'Active',
  dueDate: '2000-01-03',
  lines: [
    { targetName: 'Bracket', quantity: 20 },
    { targetName: 'Lamp', quantity: 5 },
    { targetName: 'Clip', quantity: null },
  ],
  targetUnits: 40,
  completedUnits: 24,
  writtenOffUnits: 0,
  producedUnits: 0,
  plates: {
    completed: 6,
    printing: 2,
    queued: 1,
    needsAction: 1,
    writtenOff: 0,
  },
  blockedCount: 1,
};

function stubRuns(items: ProductionRunSummaryResponse[]) {
  const requests: URL[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = new URL(input.toString(), 'http://localhost');
      requests.push(url);
      return new Response(
        JSON.stringify({
          items,
          totalCount: items.length,
          statusCounts: { Open: items.length, Closed: 41, Cancelled: 3 },
        }),
        { status: 200, headers: { 'Content-Type': 'application/json' } },
      );
    }),
  );
  return requests;
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

  it('invites a first run when nothing is open', async () => {
    stubRuns([]);
    renderPage();
    expect(await screen.findByText('No open runs')).toBeInTheDocument();
  });

  it('lists open runs by due date, with their plates, lines and what needs a decision', async () => {
    const requests = stubRuns([order]);
    renderPage();

    const row = (
      await screen.findByRole('link', { name: 'Order #1042' })
    ).closest('tr');
    expect(row).not.toBeNull();
    const cells = within(row as HTMLElement);
    expect(cells.getByText('Bracket ×20 · Lamp ×5 · +1')).toBeInTheDocument();
    expect(
      cells.getByRole('img', {
        name: '6 of 10 plates done, 2 printing, 1 failed, 1 waiting',
      }),
    ).toBeInTheDocument();
    expect(cells.getByText('24/40 units')).toBeInTheDocument();
    expect(cells.getByText(/d overdue/)).toBeInTheDocument();
    expect(cells.getByText('1 failed')).toBeInTheDocument();
    expect(cells.getByText('1 send failed')).toBeInTheDocument();
    expect(
      screen.getByRole('tab', { name: /Closed\s*41/ }),
    ).toBeInTheDocument();

    const first = requests[0].searchParams;
    expect(first.get('status')).toBe('Open');
    expect(first.get('sortBy')).toBe('dueDate');
    expect(first.get('sortDirection')).toBe('asc');
  });

  it('switches status with the tabs and searches by name or part', async () => {
    const requests = stubRuns([order]);
    const user = userEvent.setup();
    renderPage();
    await screen.findByRole('link', { name: 'Order #1042' });

    await user.click(screen.getByRole('tab', { name: /Closed/ }));
    expect(requests.at(-1)?.searchParams.get('status')).toBe('Closed');

    await user.type(
      screen.getByRole('textbox', { name: 'Search runs by name or part' }),
      'lamp',
    );
    expect(requests.at(-1)?.searchParams.get('search')).toBe('lamp');
  });
});
