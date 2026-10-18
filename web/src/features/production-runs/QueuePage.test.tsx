import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import {
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { PreferencesProvider } from '@/lib/preferences/usePreferences';
import { QueuePage } from './QueuePage';

vi.mock('./hooks/useProductionHub', () => ({ useProductionHub: vi.fn() }));
function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}
function job(id: string, name = id) {
  return {
    jobId: id,
    partName: name,
    plateQuantity: 4,
    status: 'Queued',
    assignmentMode: 'Unassigned',
    productionRunId: 'run-1',
    runDisplayLabel: 'Bracket ×20',
  };
}
function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  const result = render(
    <QueryClientProvider client={queryClient}>
      <PreferencesProvider>
        <MemoryRouter>
          <QueuePage />
        </MemoryRouter>
      </PreferencesProvider>
    </QueryClientProvider>,
  );
  return { ...result, queryClient };
}
const NO_EXTERNAL = { items: [], totalCount: 0, statusCounts: {} };
/** The printer-started section is empty unless a test opts in with `external`. */
function stub(
  handler: (params: URLSearchParams) => Response,
  { external = false }: { external?: boolean } = {},
) {
  const fetchMock = vi.fn((input: RequestInfo | URL) => {
    const url = input.toString();
    const params = new URL(url).searchParams;
    return Promise.resolve(
      !url.includes('/api/v1/print-jobs')
        ? json({ printers: [] })
        : params.get('view') === 'external' && !external
          ? json(NO_EXTERNAL)
          : handler(params),
    );
  });
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

describe('QueuePage', () => {
  beforeEach(() => {
    vi.spyOn(Element.prototype, 'getBoundingClientRect').mockImplementation(
      function (this: Element) {
        const height = this.getAttribute('role') === 'row' ? 56 : 600;
        return {
          width: 1000,
          height,
          top: 0,
          left: 0,
          bottom: height,
          right: 1000,
          x: 0,
          y: 0,
          toJSON: () => {},
        };
      },
    );
    Object.defineProperty(HTMLElement.prototype, 'offsetHeight', {
      configurable: true,
      get() {
        return this.getAttribute('role') === 'row' ? 56 : 600;
      },
    });
    Object.defineProperty(HTMLElement.prototype, 'offsetWidth', {
      configurable: true,
      value: 1000,
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

  it('changes only the chosen queue section size and resets its page', async () => {
    const requests = stub((params) =>
      json({
        totalCount: 150,
        items: [job(`${params.get('view')}-${params.get('page')}`)],
      }),
    );
    renderPage();
    await screen.findByText('queue-1');
    const user = userEvent.setup();
    await user.click(
      screen.getByRole('button', { name: 'Next finite jobs page' }),
    );
    await screen.findByText('queue-2');
    const finite = screen.getByRole('navigation', {
      name: 'Finite jobs pagination',
    });
    await user.click(
      within(finite).getByRole('combobox', { name: 'Rows per page' }),
    );
    await user.click(screen.getByRole('option', { name: '10' }));
    expect(await screen.findByText('queue-1')).toBeInTheDocument();
    await waitFor(() =>
      expect(within(finite).getByText('1–10 of 150')).toBeInTheDocument(),
    );
    expect(
      within(
        screen.getByRole('navigation', { name: 'Backfill jobs pagination' }),
      ).getByRole('combobox', { name: 'Rows per page' }),
    ).toHaveTextContent('50');
    const urls = requests.mock.calls.map(
      ([input]) => new URL(input.toString()),
    );
    expect(
      urls.some(
        (url) =>
          url.searchParams.get('view') === 'queue' &&
          url.searchParams.get('page') === '1' &&
          url.searchParams.get('pageSize') === '10',
      ),
    ).toBe(true);
    expect(
      urls
        .filter((url) => url.searchParams.get('view') === 'backfill')
        .every((url) => url.searchParams.get('pageSize') === '50'),
    ).toBe(true);
  });

  it('pages finite jobs independently of backfill in a thousand-job queue', async () => {
    stub((params) =>
      params.get('view') === 'backfill'
        ? json({ totalCount: 1, items: [job('b1', 'Backfill part')] })
        : json({
            totalCount: 1001,
            items: [
              job(
                `finite-${params.get('page')}`,
                `Page ${params.get('page')} part`,
              ),
            ],
          }),
    );
    renderPage();
    expect(await screen.findByText('Page 1 part')).toBeInTheDocument();
    expect(screen.getByText('Backfill part')).toBeInTheDocument();
    fireEvent.click(
      screen.getByRole('button', { name: 'Next finite jobs page' }),
    );
    expect(await screen.findByText('Page 2 part')).toBeInTheDocument();
    expect(screen.getByText('51–100 of 1001')).toBeInTheDocument();
    expect(screen.getByText('Backfill part')).toBeInTheDocument();
    fireEvent.click(
      screen.getByRole('button', { name: 'Previous finite jobs page' }),
    );
    expect(await screen.findByText('Page 1 part')).toBeInTheDocument();
  });

  it('collapses backfill details and restores its current page with keyboard controls', async () => {
    const user = userEvent.setup();
    stub((params) =>
      params.get('view') === 'backfill'
        ? json({
            totalCount: 60,
            items: [
              job(
                `b-${params.get('page')}`,
                `Backfill page ${params.get('page')}`,
              ),
            ],
          })
        : json({ totalCount: 1, items: [job('f1', 'Finite part')] }),
    );
    renderPage();
    expect(await screen.findByText('Backfill page 1')).toBeInTheDocument();
    await user.click(
      screen.getByRole('button', { name: 'Next backfill jobs page' }),
    );
    expect(await screen.findByText('Backfill page 2')).toBeInTheDocument();
    const toggle = screen.getByRole('button', {
      name: 'Collapse backfill jobs',
    });
    expect(toggle).toHaveAttribute('aria-expanded', 'true');
    await user.click(toggle);
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    expect(toggle).toHaveTextContent('60');
    expect(
      screen.queryByRole('region', { name: 'Backfill jobs rows' }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('navigation', { name: 'Backfill jobs pagination' }),
    ).not.toBeInTheDocument();
    expect(screen.getByText('Finite part')).toBeVisible();
    await user.keyboard('{Enter}');
    expect(toggle).toHaveAttribute('aria-expanded', 'true');
    expect(await screen.findByText('Backfill page 2')).toBeVisible();
    expect(
      screen.getByRole('navigation', { name: 'Backfill jobs pagination' }),
    ).toHaveTextContent('51–60 of 60');
  });

  it('expands when jobs arrive and respects a manual collapse across live updates', async () => {
    let totalCount = 0;
    stub((params) =>
      json({
        totalCount: params.get('view') === 'backfill' ? totalCount : 0,
        items:
          params.get('view') === 'backfill' && totalCount > 0
            ? [job('b1')]
            : [],
      }),
    );
    const { queryClient } = renderPage();
    await screen.findByText('No active finite jobs');
    expect(
      screen.getByRole('button', { name: 'Expand backfill jobs' }),
    ).toHaveAttribute('aria-expanded', 'false');
    expect(
      screen.queryByRole('region', { name: 'Backfill jobs rows' }),
    ).not.toBeInTheDocument();
    totalCount = 1;
    await queryClient.invalidateQueries();
    expect(await screen.findByText('b1')).toBeVisible();
    await userEvent.click(
      screen.getByRole('button', { name: 'Collapse backfill jobs' }),
    );
    totalCount = 2;
    await queryClient.invalidateQueries();
    expect(
      screen.getByRole('button', { name: 'Expand backfill jobs' }),
    ).toHaveAttribute('aria-expanded', 'false');
    expect(
      screen.queryByRole('region', { name: 'Backfill jobs rows' }),
    ).not.toBeInTheDocument();
    await userEvent.click(
      screen.getByRole('button', { name: 'Expand backfill jobs' }),
    );
    expect(await screen.findByText('b1')).toBeVisible();
  });

  it('shows empty feedback for both sections', async () => {
    stub(() => json({ items: [], totalCount: 0 }));
    renderPage();
    expect(
      await screen.findByText('No active finite jobs'),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('region', { name: 'Backfill jobs rows' }),
    ).not.toBeInTheDocument();
    await userEvent.click(
      screen.getByRole('button', { name: 'Expand backfill jobs' }),
    );
    expect(screen.getByText('No active backfill jobs')).toBeVisible();
  });

  it('renders one labelled table containing both types of jobs', async () => {
    stub((params) =>
      json({
        items: [
          job(params.get('view') === 'queue' ? 'Bracket' : 'Backfill bracket'),
        ],
        totalCount: 1,
      }),
    );
    renderPage();
    expect(await screen.findByText('Bracket')).toBeInTheDocument();
    expect(screen.getByText('Backfill bracket')).toBeInTheDocument();
    expect(screen.getAllByRole('table')).toHaveLength(1);
    expect(
      screen.getByRole('columnheader', { name: 'Units' }),
    ).toBeInTheDocument();
  });

  it('uses server-wide counts and resets both pages when filtering', async () => {
    const fetchMock = stub((params) =>
      json({
        totalCount: 1001,
        statusCounts: {
          Queued: 1001,
          Printing: 4,
          Paused: 2,
          Completed: 10,
          Failed: 3,
          Cancelled: 5,
        },
        items: [job(params.get('view') ?? 'job')],
      }),
    );
    renderPage();
    expect(
      await screen.findByRole('tab', { name: 'Active 2014' }),
    ).toBeInTheDocument();
    fireEvent.click(
      screen.getByRole('button', { name: 'Next finite jobs page' }),
    );
    await screen.findByText('51–100 of 1001');
    await userEvent.click(screen.getByRole('tab', { name: 'Cancelled 10' }));
    await waitFor(() =>
      expect(
        fetchMock.mock.calls.filter(
          ([url]) =>
            url.toString().includes('status=Cancelled') &&
            url.toString().includes('page=1'),
        ).length,
      ).toBe(3),
    );
    fireEvent.change(
      screen.getByRole('textbox', { name: 'Search jobs by part name' }),
      { target: { value: 'Bracket' } },
    );
    await waitFor(() =>
      expect(
        fetchMock.mock.calls.filter(([url]) =>
          url.toString().includes('search=Bracket'),
        ).length,
      ).toBe(3),
    );
  });

  it('keeps the healthy section visible when the other fails and offers retry', async () => {
    let fail = true;
    stub((params) =>
      params.get('view') === 'backfill'
        ? json({ totalCount: 1, items: [job('Healthy backfill')] })
        : fail
          ? json({ detail: 'Unavailable' }, 500)
          : json({ totalCount: 0, items: [] }),
    );
    renderPage();
    expect(
      await screen.findByText('Could not load finite jobs'),
    ).toBeInTheDocument();
    expect(screen.getByText('Healthy backfill')).toBeInTheDocument();
    fail = false;
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(
      await screen.findByText('No active finite jobs'),
    ).toBeInTheDocument();
  });

  it('virtualizes a full page rather than mounting every job', async () => {
    stub((params) =>
      json({
        totalCount: 1001,
        items:
          params.get('view') === 'backfill'
            ? []
            : Array.from({ length: 50 }, (_, index) =>
                job(`job-${index}`, `Virtual part ${index}`),
              ),
      }),
    );
    renderPage();
    await screen.findByText('Virtual part 0');
    expect(screen.getAllByText(/Virtual part/).length).toBeLessThan(50);
  });
  it('restricts drag reordering to queued jobs in the same section', async () => {
    const fetchMock = stub((params) =>
      json({
        totalCount: params.get('view') === 'queue' ? 2 : 1,
        statusCounts: { Queued: params.get('view') === 'queue' ? 2 : 1 },
        items:
          params.get('view') === 'queue' ? [job('f1'), job('f2')] : [job('b1')],
      }),
    );
    renderPage();
    await screen.findByText('f1');
    await userEvent.click(screen.getByRole('tab', { name: 'Queued 3' }));
    const source = screen.getByText('f1').closest('[role="row"]');
    const target = screen.getByText('f2').closest('[role="row"]');
    const backfill = screen.getByText('b1').closest('[role="row"]');
    if (!source || !target || !backfill) throw new Error('Missing queue row');
    fireEvent.dragStart(source);
    fireEvent.drop(backfill);
    expect(
      fetchMock.mock.calls.some(([url]) => url.toString().includes('/reorder')),
    ).toBe(false);
    fireEvent.dragStart(source);
    fireEvent.drop(target);
    await waitFor(() =>
      expect(
        fetchMock.mock.calls.some(([url]) =>
          url.toString().includes('/f1/reorder'),
        ),
      ).toBe(true),
    );
  });

  it('clamps a section page when a live update shrinks its result set', async () => {
    let totalCount = 51;
    stub((params) =>
      params.get('view') === 'backfill'
        ? json({ totalCount: 0, items: [] })
        : json({ totalCount, items: [job(`page-${params.get('page')}`)] }),
    );
    const { queryClient } = renderPage();
    await screen.findByText('page-1');
    await userEvent.click(
      screen.getByRole('button', { name: 'Next finite jobs page' }),
    );
    await screen.findByText('page-2');
    totalCount = 1;
    await queryClient.invalidateQueries();
    expect(await screen.findByText('page-1')).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.getAllByRole('button', { name: 'Page 1' })).toHaveLength(1),
    );
  });
  it('lists printer-started prints in their own section and counts them in the tabs', async () => {
    stub(
      (params) =>
        params.get('view') === 'external'
          ? json({
              totalCount: 1,
              statusCounts: { Printing: 1 },
              items: [
                {
                  jobId: 'ext-1',
                  partName: 'benchy.gcode.3mf',
                  source: 'External',
                  status: 'Printing',
                  startedAtUtc: '2026-10-18T07:00:00Z',
                },
              ],
            })
          : json({ totalCount: 0, statusCounts: {}, items: [] }),
      { external: true },
    );
    renderPage();
    const link = await screen.findByRole('link', { name: 'benchy.gcode.3mf' });
    expect(link).toHaveAttribute('href', '/jobs/ext-1');
    const row = link.closest('[role="row"]') as HTMLElement;
    expect(within(row).getByText('Printing')).toBeInTheDocument();
    expect(within(row).getByText('Printer removed')).toBeInTheDocument();
    expect(within(row).getByText(/Started/)).toBeInTheDocument();
    expect(
      screen.getByRole('rowgroup', { name: 'Printer-started prints' }),
    ).toBeInTheDocument();
    expect(
      await screen.findByRole('tab', { name: 'Printing 1' }),
    ).toBeInTheDocument();
  });

  it('hides the printer-started section on the Queued tab', async () => {
    stub(() => json({ totalCount: 0, statusCounts: {}, items: [] }));
    renderPage();
    await screen.findByText('No active finite jobs');
    expect(
      screen.getByRole('rowgroup', { name: 'Printer-started prints' }),
    ).toBeInTheDocument();
    await userEvent.click(screen.getByRole('tab', { name: /^Queued/ }));
    expect(
      screen.queryByRole('rowgroup', { name: 'Printer-started prints' }),
    ).not.toBeInTheDocument();
  });
});
