import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type {
  SpoolResponse,
  VariantStockResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { SpoolsPage } from './SpoolsPage';

const SPOOLS: SpoolResponse[] = [
  {
    id: 's-1',
    label: null,
    colorName: 'Black',
    colorHex: '#0b0b0b',
    material: 'PLA Basic',
    vendorName: 'Bambu',
    spoolWeightGrams: 1000,
    remainingWeightGrams: 820,
    state: 'Using',
    feedSlotId: 'slot-1',
    feedSlotLabel: 'X1C-01 · AMS 1',
    hasUsageOverrun: false,
  },
  {
    id: 's-2',
    label: null,
    colorName: 'Grey',
    colorHex: '#8f8f8f',
    material: 'PLA Matte',
    vendorName: 'Polymaker',
    spoolWeightGrams: 1000,
    remainingWeightGrams: 1000,
    state: 'InStorage',
    feedSlotId: null,
    feedSlotLabel: null,
    hasUsageOverrun: false,
  },
  {
    id: 's-3',
    label: null,
    colorName: 'Red',
    colorHex: '#c81e2c',
    material: 'PolyMax PLA',
    vendorName: 'eSUN',
    spoolWeightGrams: 500,
    remainingWeightGrams: 0,
    state: 'Finished',
    feedSlotId: null,
    feedSlotLabel: null,
    hasUsageOverrun: false,
  },
];

const STOCK: VariantStockResponse[] = [
  {
    filamentVariantId: 'black',
    onHandGrams: 1820,
    activeSpoolCount: 2,
    stockValue: 36.4,
    unpricedSpoolCount: 1,
    usedLast30DaysGrams: 180,
    isLow: false,
  },
  {
    filamentVariantId: 'red',
    onHandGrams: 0,
    activeSpoolCount: 0,
    stockValue: 0,
    unpricedSpoolCount: 0,
    usedLast30DaysGrams: 0,
    isLow: true,
  },
];

function stubFetch() {
  vi.stubGlobal(
    'fetch',
    vi.fn((input: RequestInfo | URL) =>
      Promise.resolve(
        new Response(
          JSON.stringify(
            input.toString().endsWith('/api/v1/filaments')
              ? { filaments: [], stock: STOCK }
              : { spools: SPOOLS },
          ),
          { status: 200, headers: { 'Content-Type': 'application/json' } },
        ),
      ),
    ),
  );
}

function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <SpoolsPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('SpoolsPage', () => {
  beforeEach(() => {
    stubFetch();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('sums the stock into on hand, value, active spools and used tiles', async () => {
    renderPage();

    expect(screen.getByRole('heading', { name: 'Spools' })).toBeInTheDocument();
    expect(await screen.findByText('1.82 kg')).toBeInTheDocument();
    expect(screen.getByText('1 color low')).toBeInTheDocument();
    expect(screen.getByText('36.40')).toBeInTheDocument();
    expect(screen.getByText('Partial · 1 spool unpriced')).toBeInTheDocument();
    expect(screen.getByText('1 in use')).toBeInTheDocument();
    expect(screen.getByText('180 g')).toBeInTheDocument();
  });

  it('lists active spools, and finished ones only in the Finished view', async () => {
    const user = userEvent.setup();
    renderPage();

    expect(await screen.findByText('Bambu')).toBeInTheDocument();
    expect(screen.getByText('Polymaker')).toBeInTheDocument();
    expect(screen.getByText(/PLA Basic/)).toBeInTheDocument();
    expect(screen.queryByText('eSUN')).not.toBeInTheDocument();
    expect(screen.getByRole('radio', { name: /^Active/ })).toBeChecked();

    await user.click(screen.getByRole('radio', { name: /^Finished/ }));

    expect(screen.getByText('eSUN')).toBeInTheDocument();
    expect(screen.queryByText('Bambu')).not.toBeInTheDocument();
  });

  it('renders state badges for using and storage spools', async () => {
    renderPage();

    const table = await screen.findByRole('table');
    expect(within(table).getByText('In use')).toBeInTheDocument();
    expect(within(table).getByText('In storage')).toBeInTheDocument();
  });

  it('searches by label, vendor, material or color, every term matching', async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText('Bambu');

    await user.type(
      screen.getByRole('textbox', {
        name: 'Search spools by label, vendor, material or color',
      }),
      'matte grey',
    );

    expect(screen.getByText('Polymaker')).toBeInTheDocument();
    expect(screen.queryByText('Bambu')).not.toBeInTheDocument();

    await user.type(
      screen.getByRole('textbox', {
        name: 'Search spools by label, vendor, material or color',
      }),
      ' nothing',
    );

    expect(screen.getByText('No spools match')).toBeInTheDocument();
  });

  it('shows an em-dash for spools without a location', async () => {
    renderPage();

    expect(await screen.findAllByText('—')).not.toHaveLength(0);
  });

  it('has an Add spool button', async () => {
    renderPage();

    expect(
      await screen.findByRole('button', { name: 'Add spool' }),
    ).toBeInTheDocument();
  });
});
