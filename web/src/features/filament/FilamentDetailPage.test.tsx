import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { lastDays, toInstants } from '@/features/stats/lib/range';
import type {
  FilamentResponse,
  SpoolResponse,
  VariantStockResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { FilamentDetailPage } from './FilamentDetailPage';

const FILAMENT: FilamentResponse = {
  id: 'f-1',
  vendorId: 'vendor-1',
  vendorName: 'Bambu',
  material: 'PLA Basic',
  materialType: 'PLA',
  pricePerKg: 24.99,
  profileAssignments: [],
  variants: [
    { id: 'v-grey', colorName: 'Grey', colorHex: '#8f8f8f', pricePerKg: 30 },
    { id: 'v-black', colorName: 'Black', colorHex: '#0b0b0b' },
  ],
};

const OTHER: FilamentResponse = {
  id: 'f-2',
  vendorName: 'eSUN',
  material: 'PETG',
  materialType: 'PETG',
  variants: [{ id: 'v-other', colorName: 'Clear', colorHex: '#ffffff' }],
};

const STOCK: VariantStockResponse[] = [
  {
    filamentVariantId: 'v-black',
    onHandGrams: 2460,
    activeSpoolCount: 3,
    stockValue: 36.4,
    unpricedSpoolCount: 1,
    usedLast30DaysGrams: 2460,
    daysLeft: 30,
    thresholdDays: 14,
    isLow: false,
  },
  {
    filamentVariantId: 'v-grey',
    onHandGrams: 0,
    activeSpoolCount: 0,
    stockValue: 0,
    unpricedSpoolCount: 0,
    usedLast30DaysGrams: 0,
    isLow: true,
  },
  {
    filamentVariantId: 'v-other',
    onHandGrams: 5000,
    activeSpoolCount: 5,
    stockValue: 100,
    unpricedSpoolCount: 0,
    usedLast30DaysGrams: 0,
    isLow: false,
  },
];

const black = (spool: SpoolResponse): SpoolResponse => ({
  filamentId: 'f-1',
  filamentVariantId: 'v-black',
  colorName: 'Black',
  colorHex: '#0b0b0b',
  spoolWeightGrams: 1000,
  ...spool,
});

const IN_PRINTERS = [
  black({
    id: 's-1',
    label: 'Shelf A',
    remainingWeightGrams: 820,
    state: 'Using',
    feedSlotLabel: 'X1C-01 · AMS 1',
  }),
];
const ON_SHELF = [
  black({
    id: 's-2',
    label: null,
    remainingWeightGrams: 1000,
    state: 'InStorage',
  }),
  black({
    id: 's-4',
    label: null,
    remainingWeightGrams: 640,
    state: 'InStorage',
  }),
];
const FINISHED = [
  black({
    id: 's-3',
    label: 'Old roll',
    remainingWeightGrams: 0,
    state: 'Finished',
  }),
];

const DAYS = Array.from(
  { length: 30 },
  (_, i) => `2026-09-${String(i + 1).padStart(2, '0')}`,
);
const DAILY = {
  days: DAYS,
  colors: [
    {
      filamentVariantId: 'v-black',
      drawnGrams: [...Array(29).fill(0), 60],
      wasteGrams: [...Array(29).fill(0), 6],
    },
  ],
};

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
  spoolId: 's-1',
  spoolLabel: 'Shelf A',
  material: 'PLA Basic',
  colorName: 'Black',
  colorHex: '#0b0b0b',
};

const REPORT = {
  rows: [
    {
      filamentVariantId: 'v-black',
      vendorName: 'Bambu',
      material: 'PLA Basic',
      colorName: 'Black',
      colorHex: '#0b0b0b',
      usedGrams: 1200,
      wasteGrams: 300,
      wasteShare: 0.2,
      writtenOffGrams: 0,
      cost: 24,
      unpricedGrams: 0,
      printCount: 4,
      notDeductedCount: 0,
    },
  ],
  total: {
    usedGrams: 1200,
    wasteGrams: 300,
    wasteShare: 0.2,
    writtenOffGrams: 0,
    cost: 24,
    unpricedGrams: 0,
    printCount: 4,
    notDeductedCount: 0,
  },
};

/** This filament's spools in one state: '0' shelf, '1' printers, otherwise finished. */
function spoolsIn(state: string | null) {
  const spools =
    state === '1' ? IN_PRINTERS : state === '0' ? ON_SHELF : FINISHED;
  return { spools, totalCount: spools.length };
}

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
        url.pathname === '/api/v1/spools'
          ? spoolsIn(url.searchParams.get('state'))
          : ({
              '/api/v1/filaments': {
                filaments: [FILAMENT, OTHER],
                stock: STOCK,
              },
              '/api/v1/filaments/shortages': {
                shortages: [
                  {
                    filamentVariantId: 'v-grey',
                    queuedGrams: 1500,
                    onHandGrams: 0,
                  },
                ],
              },
              '/api/v1/spools/history': { entries: [ENTRY], totalCount: 1 },
              '/api/v1/reports/materials': REPORT,
              '/api/v1/reports/daily-usage': DAILY,
            }[url.pathname] ?? {});
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

function Location() {
  const location = useLocation();
  return <output aria-label="Location">{location.search}</output>;
}

function renderPage(path = '/filament/f-1') {
  render(
    <QueryClientProvider
      client={
        new QueryClient({ defaultOptions: { queries: { retry: false } } })
      }
    >
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route
            path="/filament/:id"
            element={
              <>
                <FilamentDetailPage />
                <Location />
              </>
            }
          />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

const requestFor = <T extends { url: URL }>(requests: T[], pathname: string) =>
  requests.find((request) => request.url.pathname === pathname);

const spoolRequests = <T extends { url: URL }>(requests: T[]) =>
  requests
    .filter((request) => request.url.pathname === '/api/v1/spools')
    .map((request) => Object.fromEntries(request.url.searchParams));

describe('FilamentDetailPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it("totals only this filament's stock, and lists its colors most used first, with days left, use and flags", async () => {
    stubFetch();
    renderPage();

    expect(
      await screen.findByRole('heading', { name: 'Bambu · PLA Basic' }),
    ).toBeInTheDocument();
    expect(screen.getByText('PLA · USD 24.99/kg')).toBeInTheDocument();
    expect(screen.getByText('1 color low')).toBeInTheDocument();
    expect(screen.getByText('Partial · 1 spool unpriced')).toBeInTheDocument();
    expect(screen.queryByText('5 kg')).not.toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Colors' })).toHaveAttribute(
      'aria-selected',
      'true',
    );

    const rows = within(
      screen.getByRole('list', { name: 'Colors' }),
    ).getAllByRole('listitem');
    expect(rows).toHaveLength(2);
    expect(within(rows[0]).getByText('Black')).toBeInTheDocument();
    expect(rows[0]).toHaveTextContent('~30 d');
    expect(rows[0]).toHaveTextContent('3 spools');
    expect(
      await within(rows[0]).findByText('60 g in 30 d · 10% waste'),
    ).toBeVisible();
    expect(within(rows[1]).getByText('Grey')).toBeInTheDocument();
    expect(rows[1]).toHaveTextContent('Out of stock');
    expect(rows[1]).toHaveTextContent('Low');
    expect(rows[1]).toHaveTextContent('Short 1.5 kg');
  });

  it('expands a color to its spools in printers and on the shelf, and links to all of them', async () => {
    stubFetch();
    const user = userEvent.setup();
    renderPage();

    await user.click(
      await screen.findByRole('button', { name: 'Show Black spools' }),
    );

    expect(
      await screen.findByRole('link', { name: 'X1C-01 · AMS 1' }),
    ).toHaveAttribute('href', '/filament/spools/s-1');
    expect(screen.getByText('1 kg · full × 1')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: '640 g' })).toHaveAttribute(
      'href',
      '/filament/spools/s-4',
    );
    expect(screen.getByText(/24\.99\/kg default/)).toBeInTheDocument();

    await user.click(screen.getByRole('link', { name: 'All Black spools' }));

    expect(screen.getByRole('status', { name: 'Location' })).toHaveTextContent(
      '?tab=spools&color=v-black',
    );
    expect(screen.getByRole('tab', { name: 'Spools' })).toHaveAttribute(
      'aria-selected',
      'true',
    );
  });

  it('opens the tab the URL names', async () => {
    stubFetch();
    renderPage('/filament/f-1?tab=history');

    expect(await screen.findByRole('tab', { name: 'History' })).toHaveAttribute(
      'aria-selected',
      'true',
    );
  });

  it('falls back to Colors for an unknown tab', async () => {
    stubFetch();
    renderPage('/filament/f-1?tab=nope');

    expect(await screen.findByRole('tab', { name: 'Colors' })).toHaveAttribute(
      'aria-selected',
      'true',
    );
  });

  it('lists spools in printers one by one, counts identical full rolls on the shelf, and lists partial ones', async () => {
    const requests = stubFetch();
    const user = userEvent.setup();
    renderPage('/filament/f-1?tab=spools');

    expect(
      await screen.findByRole('link', { name: 'Shelf A' }),
    ).toHaveAttribute('href', '/filament/spools/s-1');
    const groups = screen.getByRole('list', { name: 'Full rolls' });
    expect(within(groups).getByText('× 1')).toBeInTheDocument();
    expect(screen.getByText('Partial · 1')).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: /^Active/ })).toHaveTextContent(
      '3',
    );
    await waitFor(() =>
      expect(
        screen.getByRole('radio', { name: /^Finished/ }),
      ).toHaveTextContent('1'),
    );
    expect(spoolRequests(requests)).toEqual(
      expect.arrayContaining([
        { filamentId: 'f-1', state: '1' },
        { filamentId: 'f-1', state: '0' },
      ]),
    );

    await user.click(
      within(groups).getByRole('button', {
        name: 'Show Black · 1 kg · full spools',
      }),
    );

    expect(
      await within(groups).findByRole('link', { name: 'Black' }),
    ).toHaveAttribute('href', '/filament/spools/s-2');
  });

  it('pages finished spools from the server, newest first, keeping the color filter', async () => {
    const requests = stubFetch();
    const user = userEvent.setup();
    renderPage('/filament/f-1?tab=spools&color=v-black');

    await user.click(await screen.findByRole('radio', { name: /^Finished/ }));

    expect(await screen.findByText('Old roll')).toBeInTheDocument();
    expect(screen.queryByText('Shelf A')).not.toBeInTheDocument();
    expect(spoolRequests(requests)).toContainEqual({
      filamentId: 'f-1',
      state: '2',
      newestFirst: 'true',
      page: '1',
      pageSize: '25',
      filamentVariantId: 'v-black',
    });
  });

  it('searches spools by label or color', async () => {
    stubFetch();
    const user = userEvent.setup();
    renderPage('/filament/f-1?tab=spools');

    await user.type(
      await screen.findByRole('textbox', {
        name: 'Search spools by label or color',
      }),
      'shelf',
    );

    expect(screen.getByRole('link', { name: 'Shelf A' })).toBeInTheDocument();
    expect(
      screen.queryByRole('list', { name: 'Full rolls' }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('heading', { name: /^Partial/ }),
    ).not.toBeInTheDocument();
  });

  it('reports usage per color of this filament, and exports it', async () => {
    const requests = stubFetch();
    stubDownload();
    const user = userEvent.setup();
    renderPage('/filament/f-1?tab=usage');

    const table = await screen.findByRole('table', {
      name: 'Filament by color',
    });
    expect(within(table).getByText('Black')).toBeVisible();
    expect(
      Object.fromEntries(
        requestFor(requests, '/api/v1/reports/materials')?.url.searchParams ??
          [],
      ),
    ).toEqual({ ...toInstants(lastDays(30)), filamentId: 'f-1' });

    await user.click(screen.getByRole('button', { name: 'Export CSV' }));

    await waitFor(() =>
      expect(
        Object.fromEntries(
          requestFor(requests, '/api/v1/reports/materials.csv')?.url
            .searchParams ?? [],
        ),
      ).toEqual({ ...toInstants(lastDays(30)), filamentId: 'f-1' }),
    );
  });

  it("lists this filament's history, linking the spool and the print, and exports the filtered rows", async () => {
    const requests = stubFetch();
    const click = stubDownload();
    const user = userEvent.setup();
    renderPage('/filament/f-1?tab=history&color=v-black');

    expect(
      await screen.findByRole('link', { name: 'Shelf A' }),
    ).toHaveAttribute('href', '/filament/spools/s-1');
    expect(screen.getByText('−20 g')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'benchy' })).toHaveAttribute(
      'href',
      '/jobs/print-1',
    );
    expect(
      Object.fromEntries(
        requestFor(requests, '/api/v1/spools/history')?.url.searchParams ?? [],
      ),
    ).toEqual({
      ...toInstants(lastDays(30)),
      filamentId: 'f-1',
      filamentVariantId: 'v-black',
      page: '1',
      pageSize: '25',
    });

    await user.click(screen.getByRole('radio', { name: '7 d' }));
    await user.click(screen.getByRole('combobox', { name: 'Source' }));
    await user.click(await screen.findByRole('option', { name: 'Finished' }));
    await user.click(screen.getByRole('button', { name: 'Export CSV' }));

    await waitFor(() => expect(click).toHaveBeenCalled());
    const csv = requestFor(requests, '/api/v1/spools/history.csv');
    expect(Object.fromEntries(csv?.url.searchParams ?? [])).toEqual({
      ...toInstants(lastDays(7)),
      filamentId: 'f-1',
      filamentVariantId: 'v-black',
      source: 'WriteOff',
    });
    expect(csv?.init?.credentials).toBe('include');
    expect((click.mock.contexts[0] as HTMLAnchorElement).download).toBe(
      'from-server.csv',
    );
  });

  it('never sends a color the filament does not have', async () => {
    const requests = stubFetch();
    renderPage('/filament/f-1?tab=history&color=v-other');

    await screen.findByRole('link', { name: 'Shelf A' });

    const search = requestFor(requests, '/api/v1/spools/history')?.url.search;
    expect(search).not.toContain('filamentVariantId');
    expect(search).not.toContain('null');
    expect(
      spoolRequests(requests).some((params) => 'filamentVariantId' in params),
    ).toBe(false);
  });

  it('says when the filament does not exist', async () => {
    stubFetch();
    renderPage('/filament/missing');

    expect(await screen.findByText('Filament not found')).toBeInTheDocument();
  });
});
