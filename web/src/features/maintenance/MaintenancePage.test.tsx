import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Toaster } from '@/components/ui/sonner';
import { AccessContext } from '@/lib/auth/AccessContext';
import { MaintenancePage } from './MaintenancePage';

const base = {
  taskId: 't1',
  taskName: 'Lubricate rods',
  cycleStartedAtUtc: '2026-10-01T00:00:00Z',
  lastDoneAtUtc: null,
  untrackedHours: 0,
  warnAtPercent: 80,
};
const ENROLLMENTS = [
  {
    ...base,
    id: 'e1',
    printerId: 'p1',
    printerName: 'Bay 1',
    state: 'Overdue',
    drivingTrigger: 'Hours',
    used: 332,
    interval: 320,
    remaining: -12,
  },
  {
    ...base,
    id: 'e2',
    printerId: 'p2',
    printerName: 'Bay 2',
    state: 'DueSoon',
    drivingTrigger: 'Days',
    used: 27,
    interval: 30,
    remaining: 3,
  },
  {
    ...base,
    id: 'e3',
    printerId: 'p3',
    printerName: 'Bay 3',
    state: 'Ok',
    drivingTrigger: 'Prints',
    used: 3,
    interval: 50,
    remaining: 47,
  },
];

function stubFetch() {
  const requests: { url: string; method?: string; body?: unknown }[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = input.toString();
      requests.push({
        url,
        method: init?.method,
        body: init?.body ? JSON.parse(init.body as string) : undefined,
      });
      const json = (body: unknown) =>
        Promise.resolve(
          new Response(JSON.stringify(body), {
            status: 200,
            headers: { 'Content-Type': 'application/json' },
          }),
        );
      if (url.includes('/hubs/farm'))
        return Promise.reject(new Error('SignalR not available in tests'));
      if (url.includes('/maintenance/enrollments/complete'))
        return json({ entityIds: ['p1', 'p2'], count: 2 });
      if (url.includes('/maintenance/enrollments'))
        return json({ items: ENROLLMENTS });
      if (url.includes('/maintenance/tasks'))
        return json({
          items: [
            {
              id: 't1',
              name: 'Lubricate rods',
              everyPrintHours: 320,
              everyDays: 30,
              everyPrints: 50,
              warnAtPercent: 80,
              printerIds: ['p1', 'p2', 'p3'],
              worstState: 'Overdue',
            },
          ],
        });
      if (url.includes('/maintenance/logs'))
        return json({ items: [], totalCount: 0 });
      return json({ printers: [] });
    }),
  );
  return requests;
}

function renderPage(access?: ReadonlySet<string>) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  function Wrapper({ children }: { children: ReactNode }) {
    return (
      <AccessContext.Provider value={access}>
        <QueryClientProvider client={queryClient}>
          <MemoryRouter>{children}</MemoryRouter>
          <Toaster />
        </QueryClientProvider>
      </AccessContext.Provider>
    );
  }
  return render(<MaintenancePage />, { wrapper: Wrapper });
}

describe('MaintenancePage', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('lists what needs attention, with progress and KPIs', async () => {
    stubFetch();
    renderPage();

    const table = within(await screen.findByRole('table'));
    expect(table.getByRole('link', { name: 'Bay 1' })).toHaveAttribute(
      'href',
      '/printers/p1',
    );
    expect(
      table.getByText('332 / 320 h · Overdue by 12 h'),
    ).toBeInTheDocument();
    expect(table.getByText('27 / 30 d · 3 d left')).toBeInTheDocument();
    expect(table.queryByText('Bay 3')).not.toBeInTheDocument();
    expect(table.getAllByText('Baseline Oct 1, 2026').length).toBe(2);
    expect(screen.getByText('Printers enrolled').nextSibling).toHaveTextContent(
      '3',
    );

    const user = userEvent.setup();
    await user.click(screen.getByRole('tab', { name: /^All/ }));
    expect(screen.getByRole('table')).toHaveTextContent('Bay 3');
    await user.click(screen.getByRole('tab', { name: /^Tasks/ }));
    expect(
      await screen.findByText('320 h · 30 d · 50 prints'),
    ).toBeInTheDocument();
  });

  it('marks the selected rows done in one request', async () => {
    const requests = stubFetch();
    renderPage();
    const user = userEvent.setup();

    await user.click(await screen.findByRole('checkbox', { name: /Bay 1/ }));
    await user.click(screen.getByRole('checkbox', { name: /Bay 2/ }));
    await user.click(screen.getByRole('button', { name: 'Mark done' }));
    const dialog = within(await screen.findByRole('dialog'));
    await user.type(dialog.getByLabelText('Notes'), 'Lithium grease');
    await user.click(dialog.getByRole('button', { name: 'Mark done' }));

    await waitFor(() =>
      expect(
        requests.find((r) => r.url.includes('/enrollments/complete'))?.body,
      ).toEqual({
        enrollmentIds: ['e1', 'e2'],
        performedAtUtc: null,
        notes: 'Lithium grease',
        performedByNote: null,
        cost: null,
      }),
    );
    expect(await screen.findByText('2 tasks marked done.')).toBeInTheDocument();
  });

  it('is read-only for a viewer', async () => {
    stubFetch();
    renderPage(new Set(['maintenance:view', 'printers:list']));
    await screen.findByRole('table');
    expect(
      screen.queryByRole('button', { name: /^Mark .* done$/ }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Add task' }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /^Actions for/ }),
    ).not.toBeInTheDocument();
  });
});
