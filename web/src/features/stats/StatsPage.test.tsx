import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { lastDays, toInstants } from './lib/range';
import { StatsPage } from './StatsPage';

const activity = (printing: number, noData = 0) => ({
  printing,
  paused: 1,
  awaitingClear: 2,
  idle: 5,
  offline: 2,
  noData,
  observed: printing + 10,
  utilization: printing / (printing + 10),
});

const summary = (prints: number, completed: number, printing: number) => ({
  prints,
  completed,
  failed: prints - completed,
  cancelled: 0,
  usedGrams: 1200,
  wasteGrams: 300,
  wasteMachineHours: 4.5,
  units: completed * 4,
  activity: activity(printing),
  queueWaitP50Minutes: 90,
  queueWaitP95Minutes: 600,
  estimateRatio: 0.93,
  plateClearMinutes: 18,
  successRate: completed / prints,
  wasteShare: 0.2,
});

const STATS = {
  current: summary(10, 8, 40),
  previous: summary(8, 6, 30),
  days: [
    {
      day: '2026-10-24',
      completed: 3,
      failed: 1,
      cancelled: 0,
      usedGrams: 500,
      wasteGrams: 100,
      units: 12,
      printingHours: 20,
      observedHours: 30,
    },
    {
      day: '2026-10-25',
      completed: 5,
      failed: 1,
      cancelled: 0,
      usedGrams: 700,
      wasteGrams: 200,
      units: 20,
      printingHours: 20,
      observedHours: 30,
    },
  ],
  printers: [
    {
      printerId: 'p02',
      name: 'P02',
      prints: 6,
      completed: 5,
      failed: 1,
      cancelled: 0,
      usedGrams: 800,
      activity: activity(30, 6),
    },
  ],
  failureCodes: [{ code: '0300_8004', message: 'Filament runout', prints: 2 }],
};

const MATERIALS = {
  rows: [
    {
      filamentVariantId: 'red',
      vendorName: 'Bambu',
      material: 'PLA Basic',
      colorName: 'Red',
      colorHex: '#ff0000',
      usedGrams: 1200,
      wasteGrams: 300,
      writtenOffGrams: 0,
      cost: 24,
      unpricedGrams: 150,
      printCount: 4,
      notDeductedCount: 0,
    },
  ],
  total: {
    usedGrams: 1200,
    wasteGrams: 300,
    writtenOffGrams: 50,
    cost: 24,
    unpricedGrams: 150,
    printCount: 5,
    notDeductedCount: 0,
  },
};

function stubFetch(stats: object = STATS) {
  const requests: URL[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn((input: RequestInfo | URL) => {
      const url = new URL(input.toString(), 'http://localhost');
      requests.push(url);
      if (url.pathname.endsWith('.csv'))
        return Promise.resolve(new Response('csv'));
      const body =
        url.pathname === '/api/v1/reports/stats'
          ? stats
          : url.pathname === '/api/v1/reports/materials'
            ? MATERIALS
            : {};
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

  it('asks for the last 30 local days in the browser time zone', async () => {
    const requests = stubFetch();
    renderPage();

    await screen.findByText('Prints per day');
    const stats = requests.find(
      (url) => url.pathname === '/api/v1/reports/stats',
    );
    const { from, to } = toInstants(lastDays(30));
    expect(stats?.searchParams.get('from')).toBe(from);
    expect(stats?.searchParams.get('to')).toBe(to);
    expect(stats?.searchParams.get('timeZone')).toBe(
      Intl.DateTimeFormat().resolvedOptions().timeZone,
    );
    expect(screen.getByText(/vs previous 30 d/)).toBeVisible();
  });

  it('leads with the figures against the previous range', async () => {
    stubFetch();
    renderPage();

    const prints = (
      await screen.findByText('Prints', { selector: 'span' })
    ).closest('[data-slot="card"]') as HTMLElement;
    expect(within(prints).getByText('10')).toBeVisible();
    expect(prints).toHaveTextContent('Up25%');

    const success = screen
      .getByText('Success', { selector: 'span' })
      .closest('[data-slot="card"]') as HTMLElement;
    expect(success).toHaveTextContent('80%');
    expect(success).toHaveTextContent('Up5.0 pt');

    // More waste is the bad direction.
    const waste = screen
      .getByText('Waste', { selector: 'span' })
      .closest('[data-slot="card"]') as HTMLElement;
    expect(within(waste).getByText('No change')).toBeVisible();
  });

  it('shows printer time, failures, materials and each printer', async () => {
    stubFetch();
    stubDownload();
    renderPage();
    const user = userEvent.setup();

    const time = (await screen.findByText('Printer time')).closest(
      '[data-slot="card"]',
    ) as HTMLElement;
    expect(time).toHaveTextContent('Printing40 h80%');
    expect(time).toHaveTextContent('Plate clear, median18m');

    expect(screen.getByText('0300_8004')).toBeVisible();
    expect(screen.getByText('Filament runout')).toBeVisible();

    const materials = (await screen.findByText('Materials')).closest(
      '[data-slot="card"]',
    ) as HTMLElement;
    expect(
      await within(materials).findByText('Partial · 150 g unpriced'),
    ).toBeVisible();
    expect(within(materials).getByText('PLA Basic · Red')).toBeVisible();

    const printers = screen.getByRole('table', { name: 'Printers' });
    expect(within(printers).getByRole('link', { name: 'P02' })).toHaveAttribute(
      'href',
      '/printers/p02',
    );
    expect(
      within(printers).getByRole('img', { name: /^Printing 75%/ }),
    ).toBeInTheDocument();

    const requests = stubFetch();
    await user.click(
      within(materials).getByRole('button', { name: 'Export CSV' }),
    );
    await waitFor(() =>
      expect(
        requests.find((url) => url.pathname === '/api/v1/reports/materials.csv')
          ?.search,
      ).toBe(`?${new URLSearchParams(toInstants(lastDays(30)))}`),
    );
  });

  it('says so when no print ended in the range', async () => {
    stubFetch({
      ...STATS,
      current: { ...summary(1, 1, 0), prints: 0, completed: 0, failed: 0 },
      days: [],
      failureCodes: [],
    });
    renderPage();

    expect(
      await screen.findByText('No prints ended in this range.'),
    ).toBeVisible();
    expect(screen.getByText('None reported.')).toBeVisible();
  });
});

function stubDownload() {
  URL.createObjectURL = vi.fn(() => 'blob:csv');
  URL.revokeObjectURL = vi.fn();
  return vi
    .spyOn(HTMLAnchorElement.prototype, 'click')
    .mockImplementation(() => {});
}
