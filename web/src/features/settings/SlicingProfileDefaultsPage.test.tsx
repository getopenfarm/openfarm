import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Toaster } from '@/components/ui/sonner';
import { SlicingProfileDefaultsPage } from './SlicingProfileDefaultsPage';

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

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

  return render(<SlicingProfileDefaultsPage />, { wrapper: Wrapper });
}

const machineProfiles = [
  {
    id: 'm1',
    name: 'BIQU B1 (0.4 nozzle)',
    sourceFormat: 'OrcaSlicer',
    baseProfileId: null,
    baseProfileName: null,
    hasUnresolvedBaseInChain: false,
    supportedMaterials: [],
  },
];

const filamentProfiles = [
  {
    id: 'f1',
    name: 'Generic PLA',
    sourceFormat: 'OrcaSlicer',
    baseProfileId: null,
    baseProfileName: null,
    hasUnresolvedBaseInChain: false,
    compatiblePrinterNames: [],
  },
];

describe('SlicingProfileDefaultsPage', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('shows nothing-configured state with links to Profiles', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL) => {
        const url = typeof input === 'string' ? input : input.toString();
        if (url.includes('/settings/slicing-profile-defaults')) {
          return Promise.resolve(
            jsonResponse({ defaultMachineProfile: null, filamentDefaults: [] }),
          );
        }
        if (url.includes('/profiles/machine')) {
          return Promise.resolve(jsonResponse({ profiles: machineProfiles }));
        }
        if (url.includes('/profiles/filament')) {
          return Promise.resolve(jsonResponse({ profiles: filamentProfiles }));
        }
        return Promise.reject(new Error(`Unexpected fetch: ${url}`));
      }),
    );

    renderPage();

    expect(
      await screen.findByRole('combobox', { name: 'Machine profile' }),
    ).toBeInTheDocument();
    expect(screen.getByText('Choose a machine profile')).toBeInTheDocument();
  });

  it('sets the default machine profile via PUT and shows the paired process on reload', async () => {
    let defaultMachineProfile: unknown = null;
    const fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (
        url.includes('/settings/default-machine-profile') &&
        init?.method === 'PUT'
      ) {
        defaultMachineProfile = {
          id: 'm1',
          name: 'BIQU B1 (0.4 nozzle)',
          defaultProcessProfile: {
            id: 'p1',
            name: '0.20mm Standard @BIQU B1',
          },
        };
        return Promise.resolve(
          jsonResponse({
            machineProfileId: 'm1',
            machineProfileName: 'BIQU B1 (0.4 nozzle)',
            defaultProcessProfileId: 'p1',
          }),
        );
      }
      if (url.includes('/settings/slicing-profile-defaults')) {
        return Promise.resolve(
          jsonResponse({
            defaultMachineProfile,
            filamentDefaults: [],
          }),
        );
      }
      if (url.includes('/profiles/machine')) {
        return Promise.resolve(jsonResponse({ profiles: machineProfiles }));
      }
      if (url.includes('/profiles/filament')) {
        return Promise.resolve(jsonResponse({ profiles: filamentProfiles }));
      }
      return Promise.reject(new Error(`Unexpected fetch: ${url}`));
    });
    vi.stubGlobal('fetch', fetchMock);

    const user = userEvent.setup();
    renderPage();

    await user.click(
      await screen.findByRole('combobox', { name: 'Machine profile' }),
    );
    await user.click(
      await screen.findByRole('option', { name: 'BIQU B1 (0.4 nozzle)' }),
    );
    await user.click(
      screen.getAllByRole('button', { name: 'Save default' })[0],
    );

    await waitFor(() => {
      const putCall = fetchMock.mock.calls.find(
        ([reqInput, reqInit]) =>
          (typeof reqInput === 'string'
            ? reqInput
            : reqInput.toString()
          ).includes('/settings/default-machine-profile') &&
          (reqInit as RequestInit | undefined)?.method === 'PUT',
      );
      expect(putCall).toBeDefined();
    });

    expect(
      await screen.findByText(
        /Paired process profile:\s*0\.20mm Standard @BIQU B1/,
      ),
    ).toBeInTheDocument();
  });

  it('sets and then replaces a filament default', async () => {
    let filamentDefaults: unknown[] = [];
    const fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (
        url.includes('/settings/filament-defaults/') &&
        init?.method === 'PUT'
      ) {
        filamentDefaults = [{ materialType: 'PLA', filamentProfileId: 'f1' }];
        return Promise.resolve(
          jsonResponse({ materialType: 'PLA', filamentProfileId: 'f1' }),
        );
      }
      if (url.includes('/settings/slicing-profile-defaults')) {
        return Promise.resolve(
          jsonResponse({
            defaultMachineProfile: null,
            filamentDefaults: filamentDefaults.length
              ? [
                  {
                    materialType: 'PLA',
                    filamentProfileId: 'f1',
                    filamentProfileName: 'Generic PLA',
                  },
                ]
              : [],
          }),
        );
      }
      if (url.includes('/profiles/machine')) {
        return Promise.resolve(jsonResponse({ profiles: machineProfiles }));
      }
      if (url.includes('/profiles/filament')) {
        return Promise.resolve(jsonResponse({ profiles: filamentProfiles }));
      }
      return Promise.reject(new Error(`Unexpected fetch: ${url}`));
    });
    vi.stubGlobal('fetch', fetchMock);

    const user = userEvent.setup();
    renderPage();

    await user.type(await screen.findByLabelText('Material type'), 'PLA');
    await user.click(
      screen.getByRole('combobox', { name: 'Filament profile' }),
    );
    await user.click(
      await screen.findByRole('option', { name: 'Generic PLA' }),
    );
    await user.click(
      screen.getAllByRole('button', { name: 'Save default' })[1],
    );

    await waitFor(() => {
      const putCall = fetchMock.mock.calls.find(
        ([reqInput, reqInit]) =>
          (typeof reqInput === 'string'
            ? reqInput
            : reqInput.toString()
          ).includes('/settings/filament-defaults/PLA') &&
          (reqInit as RequestInit | undefined)?.method === 'PUT',
      );
      expect(putCall).toBeDefined();
    });

    expect(await screen.findByText('PLA')).toBeInTheDocument();
    expect(screen.getByText('Generic PLA')).toBeInTheDocument();
  });
});
