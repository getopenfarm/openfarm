import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
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

const REPORT = {
  rows: [
    {
      filamentVariantId: 'red',
      vendorName: 'Bambu',
      material: 'PLA Basic',
      colorName: 'Red',
      colorHex: '#ff0000',
      usedGrams: 1200,
      wasteGrams: 300,
      wasteShare: 0.2,
      writtenOffGrams: 0,
      cost: 24,
      unpricedGrams: 150,
      printCount: 4,
      notDeductedCount: 0,
    },
    {
      filamentVariantId: null,
      usedGrams: 0,
      wasteGrams: 0,
      wasteShare: null,
      writtenOffGrams: 0,
      cost: 0,
      unpricedGrams: 0,
      printCount: 1,
      notDeductedCount: 2,
    },
  ],
  total: {
    usedGrams: 1200,
    wasteGrams: 300,
    wasteShare: 0.2,
    writtenOffGrams: 0,
    cost: 24,
    unpricedGrams: 150,
    printCount: 5,
    notDeductedCount: 2,
  },
};

function stubFetch() {
  const requests: { url: URL; init?: RequestInit }[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = new URL(input.toString(), 'http://localhost');
      requests.push({ url, init });
      if (url.pathname.endsWith('.csv'))
        return Promise.resolve(
          new Response('csv', {
            headers: {
              'Content-Disposition': 'attachment; filename=from-server.csv',
            },
          }),
        );
      const body =
        url.pathname === '/api/v1/spools/history'
          ? { entries: [ENTRY], totalCount: 1 }
          : url.pathname === '/api/v1/reports/materials'
            ? REPORT
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

function stubDownload() {
  URL.createObjectURL = vi.fn(() => 'blob:csv');
  URL.revokeObjectURL = vi.fn();
  return vi
    .spyOn(HTMLAnchorElement.prototype, 'click')
    .mockImplementation(() => {});
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

  it('shows the last 30 days by material, with a total, and exports it', async () => {
    const requests = stubFetch();
    stubDownload();
    renderPage();
    const user = userEvent.setup();

    const table = await screen.findByRole('table', {
      name: 'Filament by material',
    });
    expect(within(table).getByText('Bambu PLA Basic · Red')).toBeVisible();
    expect(within(table).getByText('No spool')).toBeVisible();
    expect(within(table).getAllByText('Partial · 150 g unpriced')).toHaveLength(
      2,
    );
    const total = within(table).getByText('Total').closest('tr');
    expect(total).toHaveTextContent('1.2 kg300 g20%0 g24.00Partial');
    await user.click(screen.getByRole('button', { name: 'Export CSV' }));

    await waitFor(() =>
      expect(
        requests.find(
          (request) => request.url.pathname === '/api/v1/reports/materials.csv',
        )?.url.search,
      ).toBe(`?${new URLSearchParams(toInstants(lastDays(30)))}`),
    );
  });

  it("lists every spool's changes in the History tab, linking the spool and the print", async () => {
    const requests = stubFetch();
    renderPage();
    await userEvent.setup().click(screen.getByRole('tab', { name: 'History' }));

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
    const click = stubDownload();
    renderPage();
    const user = userEvent.setup();

    await user.click(screen.getByRole('tab', { name: 'History' }));
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
      'from-server.csv',
    );
  });
});
