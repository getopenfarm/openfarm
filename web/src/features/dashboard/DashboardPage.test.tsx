import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, within } from '@testing-library/react';
import type { ReactNode } from 'react';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { DashboardPage } from './DashboardPage';

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

  return render(<DashboardPage />, { wrapper: Wrapper });
}

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function stubFleetFetch(printers: unknown[]) {
  const fetchMock = vi.fn((input: RequestInfo | URL) => {
    const url = typeof input === 'string' ? input : input.toString();
    if (url.includes('/hubs/farm')) {
      return Promise.reject(new Error('SignalR not available in tests'));
    }
    return Promise.resolve(jsonResponse({ printers }));
  });
  vi.stubGlobal('fetch', fetchMock);
}

describe('DashboardPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('renders the heading', async () => {
    stubFleetFetch([]);
    renderPage();
    expect(
      screen.getByRole('heading', { name: 'Dashboard' }),
    ).toBeInTheDocument();
    await screen.findByText(/Live overview of 0 printers/);
  });

  it('derives KPI counts from the real fleet', async () => {
    stubFleetFetch([
      {
        id: '1',
        name: 'X1C-01',
        brand: 'Bambu',
        modelCode: 'X1C',
        status: 'Printing',
        isReachable: true,
        printProgressPercent: 42,
      },
      {
        id: '2',
        name: 'P1S-02',
        brand: 'Bambu',
        modelCode: 'P1S',
        status: 'AwaitingClear',
        isReachable: true,
        printProgressPercent: 100,
      },
      {
        id: '3',
        name: 'MK4-03',
        brand: 'Prusa',
        modelCode: 'MK4',
        status: 'Idle',
        isReachable: true,
        printProgressPercent: null,
      },
    ]);

    renderPage();

    await screen.findByText(/Live overview of 3 printers/);

    const printingKpi = screen
      .getByText('Printing now')
      .closest('[data-slot="card"]');
    expect(printingKpi).not.toBeNull();
    expect(
      within(printingKpi as HTMLElement).getByText('1'),
    ).toBeInTheDocument();

    const awaitingKpi = screen
      .getByText('Awaiting clear')
      .closest('[data-slot="card"]');
    expect(awaitingKpi).not.toBeNull();
    expect(
      within(awaitingKpi as HTMLElement).getByText('1'),
    ).toBeInTheDocument();
  });

  it('lists actively printing printers in the currently-printing panel', async () => {
    stubFleetFetch([
      {
        id: '1',
        name: 'X1C-01',
        brand: 'Bambu',
        modelCode: 'X1C',
        status: 'Printing',
        isReachable: true,
        printProgressPercent: 42,
      },
      {
        id: '2',
        name: 'P1S-02',
        brand: 'Bambu',
        modelCode: 'P1S',
        status: 'Idle',
        isReachable: true,
        printProgressPercent: null,
      },
    ]);

    renderPage();

    const panel = screen
      .getByText('Currently printing')
      .closest('[data-slot="card"]') as HTMLElement;
    expect(panel).not.toBeNull();
    await within(panel).findByText('X1C-01');
    expect(within(panel).queryByText('P1S-02')).not.toBeInTheDocument();
  });

  it('renders the mock low-filament and maintenance-due panels without crashing', async () => {
    stubFleetFetch([]);
    renderPage();

    await screen.findByText('Blue PLA Basic');
    expect(screen.getAllByText('Low filament').length).toBeGreaterThan(0);

    expect(screen.getAllByText('Maintenance due').length).toBeGreaterThan(0);
    expect(
      await screen.findByText('No maintenance flags.'),
    ).toBeInTheDocument();
  });
});
