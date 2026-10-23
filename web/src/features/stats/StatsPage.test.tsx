import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { lastDays, toInstants } from './lib/range';
import { StatsPage } from './StatsPage';

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
        url.pathname === '/api/v1/reports/materials'
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
        <StatsPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('StatsPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('shows every filament over the last 30 days, with a total, and exports it', async () => {
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
});
