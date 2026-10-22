import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { lastDays, toInstants } from './lib/range';
import { UsagePage } from './UsagePage';

const ENTRY = {
  id: 'e-1',
  recordedAtUtc: '2026-10-20T10:00:00Z',
  changeGrams: -20,
  remainingAfterGrams: 780,
  source: 'Job',
  reason: null,
  printId: 'print-1',
  printName: 'benchy',
  printStatus: 'Completed',
  spoolId: 'spool-1',
  spoolLabel: 'Shelf A',
  material: 'PLA Basic',
  colorName: 'Red',
  colorHex: '#ff0000',
};

function stubFetch() {
  const requests: { url: URL; init?: RequestInit }[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = new URL(input.toString(), 'http://localhost');
      requests.push({ url, init });
      if (url.pathname === '/api/v1/spools/history.csv')
        return Promise.resolve(
          new Response('csv', {
            headers: {
              'Content-Disposition': 'attachment; filename=spool-history.csv',
            },
          }),
        );
      const body =
        url.pathname === '/api/v1/spools/history'
          ? { entries: [ENTRY], totalCount: 1 }
          : { filaments: [], stock: [] };
      return Promise.resolve(
        new Response(JSON.stringify(body), {
          headers: { 'Content-Type': 'application/json' },
        }),
      );
    }),
  );
  return requests;
}

function renderPage() {
  render(
    <QueryClientProvider
      client={
        new QueryClient({ defaultOptions: { queries: { retry: false } } })
      }
    >
      <MemoryRouter>
        <UsagePage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('UsagePage', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it("lists every spool's changes in the last 30 days, linking the spool and the print", async () => {
    const requests = stubFetch();
    renderPage();

    expect(
      await screen.findByRole('link', { name: 'Shelf A' }),
    ).toHaveAttribute('href', '/spools/spool-1');
    expect(screen.getByText('PLA Basic · Red')).toBeInTheDocument();
    expect(screen.getByText('−20 g')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'benchy' })).toHaveAttribute(
      'href',
      '/jobs/print-1',
    );
    const list = requests.find(
      (request) => request.url.pathname === '/api/v1/spools/history',
    );
    expect(Object.fromEntries(list?.url.searchParams ?? [])).toEqual({
      ...toInstants(lastDays(30)),
      page: '1',
      pageSize: '25',
    });
  });

  it('exports the filtered history as CSV', async () => {
    const requests = stubFetch();
    URL.createObjectURL = vi.fn(() => 'blob:csv');
    URL.revokeObjectURL = vi.fn();
    const click = vi
      .spyOn(HTMLAnchorElement.prototype, 'click')
      .mockImplementation(() => {});
    renderPage();
    const user = userEvent.setup();

    await user.click(screen.getByRole('radio', { name: '7 d' }));
    await user.click(screen.getByRole('combobox', { name: 'Source' }));
    await user.click(await screen.findByRole('option', { name: 'Finished' }));
    await user.click(screen.getByRole('button', { name: 'Export CSV' }));

    await waitFor(() => expect(click).toHaveBeenCalled());
    const csv = requests.find(
      (request) => request.url.pathname === '/api/v1/spools/history.csv',
    );
    expect(Object.fromEntries(csv?.url.searchParams ?? [])).toEqual({
      ...toInstants(lastDays(7)),
      source: 'WriteOff',
    });
    expect(csv?.init?.headers).toHaveProperty('Authorization');
    expect((click.mock.contexts[0] as HTMLAnchorElement).download).toBe(
      'spool-history.csv',
    );
  });
});
