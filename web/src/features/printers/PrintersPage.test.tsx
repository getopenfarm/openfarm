import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Toaster } from '@/components/ui/sonner';
import { PrintersPage } from './PrintersPage';

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

  return render(<PrintersPage />, { wrapper: Wrapper });
}

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

describe('PrintersPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('renders the fleet list with status and reachability', async () => {
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
              name: 'Test Printer',
              brand: 'Bambu',
              modelCode: 'P1S',
              status: 'Idle',
              isReachable: true,
              printProgressPercent: null,
            },
          ],
        }),
      );
    });
    vi.stubGlobal('fetch', fetchMock);

    renderPage();

    expect(await screen.findByText('Test Printer')).toBeInTheDocument();
    expect(screen.getByText('Idle')).toBeInTheDocument();
    expect(screen.queryByText('Offline')).not.toBeInTheDocument();
  });

  it('shows an empty state when no printers are registered', async () => {
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
        screen.getByText(/No printers registered yet/),
      ).toBeInTheDocument(),
    );
  });

  it('shows an offline indicator distinct from status when unreachable', async () => {
    const fetchMock = vi.fn((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/hubs/farm')) {
        return Promise.reject(new Error('SignalR not available in tests'));
      }
      return Promise.resolve(
        jsonResponse({
          printers: [
            {
              id: '22222222-2222-2222-2222-222222222222',
              name: 'Unreachable Printer',
              brand: 'Bambu',
              modelCode: 'P1S',
              status: 'Idle',
              isReachable: false,
              printProgressPercent: null,
            },
          ],
        }),
      );
    });
    vi.stubGlobal('fetch', fetchMock);

    renderPage();

    await screen.findByText('Unreachable Printer');
    expect(screen.getByText('Idle')).toBeInTheDocument();
    expect(screen.getByText('Offline')).toBeInTheDocument();
  });
});
