import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Toaster } from '@/components/ui/sonner';
import { PREFERENCES_STORAGE_KEY } from '@/lib/preferences/defaultPreferences';
import { PreferencesProvider } from '@/lib/preferences/usePreferences';
import { PrintersPage } from './PrintersPage';

function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });

  function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={queryClient}>
        <PreferencesProvider>
          <MemoryRouter>{children}</MemoryRouter>
          <Toaster />
        </PreferencesProvider>
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
    window.localStorage.removeItem(PREFERENCES_STORAGE_KEY);
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
    const table = within(screen.getByRole('table'));
    expect(table.getByText('Idle')).toBeInTheDocument();
    expect(table.queryByText('Offline')).not.toBeInTheDocument();
    // User Story 2 (SC-003): Edit/Remove are relocated to the detail page only.
    expect(table.queryByText('Edit')).not.toBeInTheDocument();
    expect(table.queryByText('Remove')).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /actions for/i }),
    ).not.toBeInTheDocument();

    const user = userEvent.setup();
    const search = screen.getByRole('textbox', {
      name: 'Search printers by name or model',
    });
    await user.type(search, 'missing');
    expect(screen.getByText('No printers match')).toBeInTheDocument();
    await user.clear(search);
    await user.type(search, 'P1S');
    expect(screen.getByRole('table')).toHaveTextContent('Test Printer');
    await user.click(screen.getByRole('radio', { name: /^Printing/ }));
    expect(screen.getByText('No printers match')).toBeInTheDocument();
    await user.click(screen.getByRole('radio', { name: /^All/ }));
    expect(screen.getByRole('table')).toHaveTextContent('Test Printer');
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
        screen.getByText(/Add one to see live status/),
      ).toBeInTheDocument(),
    );
  });

  it('shows an error state with a retry button when the fleet request fails', async () => {
    const fetchMock = vi.fn((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/hubs/farm')) {
        return Promise.reject(new Error('SignalR not available in tests'));
      }
      return Promise.resolve(jsonResponse({ title: 'Server error' }, 500));
    });
    vi.stubGlobal('fetch', fetchMock);

    renderPage();

    expect(
      await screen.findByText(/Could not load printers/),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
    expect(
      screen.queryByText(/Add one to see live status/),
    ).not.toBeInTheDocument();
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
    const table = within(screen.getByRole('table'));
    expect(table.getByText('Offline')).toBeInTheDocument();
    expect(table.queryByText('Idle')).not.toBeInTheDocument();
  });

  it('always renders a table without readiness controls despite a saved grid preference', async () => {
    window.localStorage.setItem(
      PREFERENCES_STORAGE_KEY,
      JSON.stringify({ listViews: { printers: 'grid' } }),
    );
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
              name: 'Finished Printer',
              brand: 'Bambu',
              modelCode: 'P1S',
              status: 'AwaitingClear',
              isReachable: true,
              printProgressPercent: null,
            },
          ],
        }),
      );
    });
    vi.stubGlobal('fetch', fetchMock);

    renderPage();

    expect(await screen.findByText('Finished Printer')).toBeInTheDocument();
    expect(screen.getByRole('table')).toBeInTheDocument();
    expect(screen.getAllByText('Awaiting clear')[0]).toBeInTheDocument();
    expect(
      screen.queryByRole('button', {
        name: /grid view|table view|mark ready|^clear$|actions for/i,
      }),
    ).not.toBeInTheDocument();
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(
      fetchMock.mock.calls.every(
        ([input]) => !input.toString().includes('/mark-ready'),
      ),
    ).toBe(true);
    expect(
      screen.queryByRole('button', { name: /^edit /i }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /^remove /i }),
    ).not.toBeInTheDocument();
  });
});
