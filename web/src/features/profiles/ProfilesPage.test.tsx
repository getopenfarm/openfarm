import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
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
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    );
  }

  return render(<ProfilesPage />, { wrapper: Wrapper });
}

describe('ProfilesPage', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('renders the Machine/Filament tabs and the Import trigger', async () => {
    stubFetch({
      '/profiles/machine': { profiles: [] },
      '/profiles/filament': { profiles: [] },
    });

    renderPage();

    expect(screen.getByText('Profiles')).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Machine' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Filament' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Import' })).toBeInTheDocument();
  });

  it('shows an empty-state message before any profiles are imported', async () => {
    stubFetch({
      '/profiles/machine': { profiles: [] },
      '/profiles/filament': { profiles: [] },
    });

    renderPage();

    expect(await screen.findByText('No machine profiles yet')).toBeInTheDocument();
    expect(
      screen.getByText('Import a slicer bundle to populate this catalog.'),
    ).toBeInTheDocument();
  });

  it('renders imported profiles in the catalog tables', async () => {
    stubFetch({
      '/profiles/machine': {
        profiles: [
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
      '/profiles/filament': { profiles: [] },
    });

    renderPage();

    expect(
      await screen.findByText('Bambu X1 Carbon 0.4 nozzle'),
    ).toBeInTheDocument();
  });

  it('opens the raw data dialog within one interaction from a catalog row (SC-006)', async () => {
    const user = userEvent.setup();
    stubFetch({
      '/profiles/machine': {
        profiles: [
          {
            id: 'm1',
            name: 'Bambu X1 Carbon 0.4 nozzle',
            sourceFormat: 'OrcaSlicer',
            baseProfileId: null,
            baseProfileName: null,
            hasUnresolvedBaseInChain: false,
            nozzleSizeMm: 0.4,
            bedSizeXMm: null,
            bedSizeYMm: null,
            maxHeightMm: null,
            hasEnclosure: null,
            supportedMaterials: [],
          },
        ],
      },
      '/profiles/filament': { profiles: [] },
      '/profiles/machine/m1': {
        id: 'm1',
        name: 'Bambu X1 Carbon 0.4 nozzle',
        sourceFormat: 'OrcaSlicer',
        baseProfileId: null,
        baseProfileName: null,
        hasUnresolvedBaseInChain: false,
        rawDataJson: '{"nozzle_diameter":["0.4"]}',
        createdAtUtc: '2026-01-01T00:00:00Z',
        nozzleSizeMm: 0.4,
        bedSizeXMm: null,
        bedSizeYMm: null,
        maxHeightMm: null,
        hasEnclosure: null,
        supportedMaterials: [],
      },
    });

    renderPage();

    const row = await screen.findByText('Bambu X1 Carbon 0.4 nozzle');
    await user.click(row);

    await waitFor(() => expect(screen.getByRole('dialog')).toBeInTheDocument());
    expect(await screen.findByText(/nozzle_diameter/)).toBeInTheDocument();
  });
});
