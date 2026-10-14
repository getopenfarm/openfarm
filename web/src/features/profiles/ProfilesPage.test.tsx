import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import type { ReactNode } from 'react';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ProfilesPage } from './ProfilesPage';

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function stubFetch(routes: Record<string, unknown>) {
  // Longest path first, so a specific route (e.g. "/profiles/machine/m1") is matched before a
  // more general one that's also a substring of it (e.g. "/profiles/machine").
  const sortedRoutes = Object.entries(routes).sort(
    ([a], [b]) => b.length - a.length,
  );
  const fetchMock = vi.fn((input: RequestInfo | URL) => {
    const url = typeof input === 'string' ? input : input.toString();
    for (const [path, body] of sortedRoutes) {
      if (url.includes(path)) {
        return Promise.resolve(jsonResponse(body));
      }
    }
    return Promise.reject(new Error(`Unexpected fetch: ${url}`));
  });
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

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

  return render(<ProfilesPage />, { wrapper: Wrapper });
}

describe('ProfilesPage', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('renders the Machine/Filament tabs (no Process tab, FR-011/FR-013) and the Import trigger', async () => {
    stubFetch({
      '/profiles/machine': { items: [] },
      '/profiles/filament': { items: [] },
    });

    renderPage();

    expect(screen.getByText('Profiles')).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Machine' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Filament' })).toBeInTheDocument();
    expect(
      screen.queryByRole('tab', { name: 'Process' }),
    ).not.toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: 'Import profiles' }),
    ).toBeInTheDocument();
  });

  it('shows an empty-state message before any profiles are imported', async () => {
    stubFetch({
      '/profiles/machine': { items: [] },
      '/profiles/filament': { items: [] },
    });

    renderPage();

    expect(
      await screen.findByText('No machine profiles yet'),
    ).toBeInTheDocument();
    expect(
      screen.getByText('Import a slicer bundle to populate this catalog.'),
    ).toBeInTheDocument();
  });

  it('renders imported profiles in the catalog tables', async () => {
    stubFetch({
      '/profiles/machine': {
        items: [
          {
            id: 'm1',
            name: 'Bambu X1 Carbon 0.4 nozzle',
            sourceFormat: 'OrcaSlicer',
            baseProfileId: null,
            baseProfileName: null,
            hasUnresolvedBaseInChain: false,
            nozzleSizeMm: 0.4,
            bedSizeXMm: 256,
            bedSizeYMm: 256,
            maxHeightMm: 256,
            hasEnclosure: true,
            supportedMaterials: [],
          },
        ],
      },
      '/profiles/filament': { items: [] },
    });

    renderPage();

    expect(
      await screen.findByText('Bambu X1 Carbon 0.4 nozzle'),
    ).toBeInTheDocument();
  });

  it('links each catalog row to its profile detail page', async () => {
    stubFetch({
      '/profiles/machine': {
        items: [{ id: 'm1', name: 'Bambu X1 Carbon 0.4 nozzle' }],
      },
      '/profiles/filament': { items: [] },
    });

    renderPage();

    expect(
      await screen.findByRole('link', { name: 'Bambu X1 Carbon 0.4 nozzle' }),
    ).toHaveAttribute('href', '/profiles/machine/m1');
  });
});
