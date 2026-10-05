import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Toaster } from '@/components/ui/sonner';
import { SlicingDefaultsPage } from './SlicingDefaultsPage';

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

  return render(<SlicingDefaultsPage />, { wrapper: Wrapper });
}

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

describe('SlicingDefaultsPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('renders the available slicer list with the effective default preselected', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse({
        default: { engine: 'OrcaSlicer', version: '2.3.0', isExplicit: false },
        available: [
          { engine: 'OrcaSlicer', version: '2.3.0', platform: 'Linux' },
          { engine: 'OrcaSlicer', version: '2.2.0', platform: 'Linux' },
          { engine: 'SuperSlicer', version: '2.5.59.13', platform: 'Linux' },
        ],
      }),
    );
    vi.stubGlobal('fetch', fetchMock);

    renderPage();

    expect(
      await screen.findByText(/No explicit default is set/),
    ).toBeInTheDocument();
    expect(screen.getByText('OrcaSlicer 2.3.0 (Linux)')).toBeInTheDocument();
  });

  it('submits the selected default via PUT', async () => {
    let currentDefault = {
      engine: 'OrcaSlicer',
      version: '2.3.0',
      isExplicit: false,
    };
    const available = [
      { engine: 'OrcaSlicer', version: '2.3.0', platform: 'Linux' },
      { engine: 'SuperSlicer', version: '2.5.59.13', platform: 'Linux' },
    ];

    const fetchMock = vi.fn((_input: RequestInfo | URL, init?: RequestInit) => {
      if (init?.method === 'PUT') {
        currentDefault = {
          engine: 'SuperSlicer',
          version: '2.5.59.13',
          isExplicit: true,
        };
        return Promise.resolve(
          jsonResponse({ default: currentDefault, available }),
        );
      }
      return Promise.resolve(
        jsonResponse({ default: currentDefault, available }),
      );
    });
    vi.stubGlobal('fetch', fetchMock);

    const user = userEvent.setup();
    renderPage();

    await screen.findByText('OrcaSlicer 2.3.0 (Linux)');

    await user.click(screen.getByRole('combobox', { name: 'Default slicer' }));
    await user.click(
      await screen.findByRole('option', {
        name: 'SuperSlicer 2.5.59.13 (Linux)',
      }),
    );
    await user.click(screen.getByRole('button', { name: 'Save default' }));

    await waitFor(() => {
      const putCalls = fetchMock.mock.calls.filter(
        ([, init]) => (init as RequestInit | undefined)?.method === 'PUT',
      );
      expect(putCalls).toHaveLength(1);
    });

    const [, putInit] = fetchMock.mock.calls.find(
      ([, init]) => (init as RequestInit | undefined)?.method === 'PUT',
    )!;
    expect(JSON.parse((putInit as RequestInit).body as string)).toEqual({
      engine: 'SuperSlicer',
      version: '2.5.59.13',
      estimationProfiles: null,
    });
  });

  it('shows the empty state when no slicer is bundled', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(jsonResponse({ default: null, available: [] }));
    vi.stubGlobal('fetch', fetchMock);

    renderPage();

    expect(await screen.findByText('No slicer available')).toBeInTheDocument();
  });
  it('clears explicit estimation defaults while retaining the slicer selection', async () => {
    let profiles: object | null = {
      machineProfileId: 'machine-id',
      processProfileId: 'process-id',
      filamentProfileId: 'filament-id',
      buildPlateType: 'TexturedPei',
    };
    const fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      if (init?.method === 'PUT') profiles = null;
      if (String(input).includes('/profiles/'))
        return Promise.resolve(
          jsonResponse({ id: 'profile', name: 'Saved profile' }),
        );
      return Promise.resolve(
        jsonResponse({
          default: { engine: 'OrcaSlicer', version: '2.3.1', isExplicit: true },
          available: [
            { engine: 'OrcaSlicer', version: '2.3.1', platform: 'MacOs' },
          ],
          estimationProfiles: profiles,
        }),
      );
    });
    vi.stubGlobal('fetch', fetchMock);
    const user = userEvent.setup();
    renderPage();
    await user.click(
      await screen.findByRole('button', { name: 'Clear estimation profiles' }),
    );
    await user.click(screen.getByRole('button', { name: 'Save default' }));
    await waitFor(() =>
      expect(
        fetchMock.mock.calls.some(([, init]) => init?.method === 'PUT'),
      ).toBe(true),
    );
    const call = fetchMock.mock.calls.find(
      ([, init]) => init?.method === 'PUT',
    );
    expect(JSON.parse(call?.[1]?.body as string)).toEqual({
      engine: 'OrcaSlicer',
      version: '2.3.1',
      estimationProfiles: null,
    });
  });

  it('changing the machine clears dependent settings and prevents incomplete saves', async () => {
    const fetchMock = vi.fn((input: RequestInfo | URL) => {
      if (String(input).includes('/profiles/machine?'))
        return Promise.resolve(
          jsonResponse({
            items: [{ id: 'new-machine', name: 'New machine' }],
            totalCount: 1,
          }),
        );
      if (String(input).includes('/profiles/'))
        return Promise.resolve(
          jsonResponse({ id: 'profile', name: 'Saved profile' }),
        );
      return Promise.resolve(
        jsonResponse({
          default: { engine: 'OrcaSlicer', version: '2.3.1', isExplicit: true },
          available: [
            { engine: 'OrcaSlicer', version: '2.3.1', platform: 'MacOs' },
          ],
          estimationProfiles: {
            machineProfileId: 'machine-id',
            processProfileId: 'process-id',
            filamentProfileId: 'filament-id',
            buildPlateType: 'TexturedPei',
          },
        }),
      );
    });
    vi.stubGlobal('fetch', fetchMock);
    const user = userEvent.setup();
    renderPage();
    await user.click(
      await screen.findByRole('button', { name: 'Estimation machine profile' }),
    );
    await user.click(
      await screen.findByRole('button', { name: /New machine/ }),
    );
    expect(screen.getByRole('button', { name: 'Save default' })).toBeDisabled();
    expect(
      screen.getByText(
        /Select machine, process, filament, and build plate before saving/,
      ),
    ).toBeInTheDocument();
  });
});
