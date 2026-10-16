import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Toaster } from '@/components/ui/sonner';
import { MaintenancePage } from './MaintenancePage';

function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });

  function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={queryClient}>
        {children}
        <Toaster />
      </QueryClientProvider>
    );
  }

  return render(<MaintenancePage />, { wrapper: Wrapper });
}

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

describe('MaintenancePage', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('cross-references mock maintenance flags onto the real fleet list', async () => {
    const fetchMock = vi.fn((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/hubs/farm')) {
        return Promise.reject(new Error('SignalR not available in tests'));
      }
      return Promise.resolve(
        jsonResponse({
          printers: [
            {
              id: '11111111-1111-1111-1111-111111111111',
              name: 'Bay 1 X1C',
              brand: 'Bambu',
              modelCode: 'X1C',
              status: 'Idle',
              isReachable: true,
            },
          ],
        }),
      );
    });
    vi.stubGlobal('fetch', fetchMock);

    renderPage();

    // The real printer name should surface on at least one matched card.
    expect((await screen.findAllByText('Bay 1 X1C')).length).toBeGreaterThan(0);
    // At least one overdue/due badge from the matched flags should render.
    expect(screen.getAllByText('Overdue').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Due soon').length).toBeGreaterThan(0);
    // Task templates section always renders regardless of fleet contents.
    expect(screen.getByText('Task templates')).toBeInTheDocument();
    expect(screen.getAllByText('Nozzle inspection').length).toBeGreaterThan(0);
  });

  it('shows an empty state for needs attention when no printers are registered', async () => {
    const fetchMock = vi.fn((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/hubs/farm')) {
        return Promise.reject(new Error('SignalR not available in tests'));
      }
      return Promise.resolve(jsonResponse({ printers: [] }));
    });
    vi.stubGlobal('fetch', fetchMock);

    renderPage();

    await waitFor(() =>
      expect(
        screen.getByText(/Add a printer to see maintenance flags/),
      ).toBeInTheDocument(),
    );
    // Task templates section is unaffected by an empty fleet.
    expect(screen.getByText('Task templates')).toBeInTheDocument();
  });
});
