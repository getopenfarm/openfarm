import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { SpoolResponse } from '@/lib/api/generated/endpoints.schemas';
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

function stubFetch() {
  vi.stubGlobal(
    'fetch',
    vi.fn(() =>
      Promise.resolve(
        new Response(JSON.stringify({ spools: SPOOLS }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }),
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
      <SpoolsPage />
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

  it('renders the page heading and subtitle counts', async () => {
    renderPage();

    expect(screen.getByRole('heading', { name: 'Spools' })).toBeInTheDocument();
    expect(await screen.findByText(/3 spools/)).toBeInTheDocument();
  });

  it('renders spool rows with vendor names and materials', async () => {
    renderPage();

    expect(await screen.findByText('Bambu')).toBeInTheDocument();
    expect(screen.getByText('Polymaker')).toBeInTheDocument();
    expect(screen.getByText('eSUN')).toBeInTheDocument();
    expect(screen.getByText(/PLA Basic/)).toBeInTheDocument();
  });

  it('renders state badges for using, storage, and finished spools', async () => {
    renderPage();

    expect(await screen.findByText('In use')).toBeInTheDocument();
    expect(screen.getByText('In storage')).toBeInTheDocument();
    expect(screen.getByText('Finished')).toBeInTheDocument();
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
