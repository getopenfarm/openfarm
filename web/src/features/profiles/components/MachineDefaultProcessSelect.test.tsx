import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { Toaster } from 'sonner';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { MachineDefaultProcessSelect } from './MachineDefaultProcessSelect';

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function renderSelect(props: {
  machineId: string;
  machineSourceFormat: string;
  defaultProcessProfileId: string | null | undefined;
}) {
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

  return render(<MachineDefaultProcessSelect {...props} />, {
    wrapper: Wrapper,
  });
}

const processProfiles = [
  {
    id: 'p1',
    name: '0.20mm Standard @BIQU B1',
    sourceFormat: 'OrcaSlicer',
    baseProfileId: null,
    baseProfileName: null,
    hasUnresolvedBaseInChain: false,
    layerHeightMm: 0.2,
    wallLoops: 3,
    compatiblePrinterNames: [],
  },
  {
    id: 'p2',
    name: '0.20mm Standard @BBL X1C',
    sourceFormat: 'BambuStudio',
    baseProfileId: null,
    baseProfileName: null,
    hasUnresolvedBaseInChain: false,
    layerHeightMm: 0.2,
    wallLoops: 3,
    compatiblePrinterNames: [],
  },
];

describe('MachineDefaultProcessSelect', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('filters the process list to the machine source format', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.resolve(jsonResponse({ profiles: processProfiles }))),
    );

    renderSelect({
      machineId: 'm1',
      machineSourceFormat: 'OrcaSlicer',
      defaultProcessProfileId: undefined,
    });

    const user = userEvent.setup();
    await user.click(screen.getByRole('combobox'));

    expect(
      await screen.findByRole('option', { name: '0.20mm Standard @BIQU B1' }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('option', { name: '0.20mm Standard @BBL X1C' }),
    ).not.toBeInTheDocument();
  });

  it('shows a note instead of an empty select when nothing matches the format', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() =>
        Promise.resolve(jsonResponse({ profiles: [processProfiles[1]] })),
      ),
    );

    renderSelect({
      machineId: 'm1',
      machineSourceFormat: 'OrcaSlicer',
      defaultProcessProfileId: undefined,
    });

    expect(
      await screen.findByText(/No OrcaSlicer process profiles imported yet/),
    ).toBeInTheDocument();
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
  });

  it('pairs a process profile and invalidates the machine detail query on success', async () => {
    const fetchMock = vi.fn((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/profiles/process')) {
        return Promise.resolve(jsonResponse({ profiles: processProfiles }));
      }
      if (url.includes('/default-process-profile')) {
        return Promise.resolve(
          jsonResponse({ id: 'm1', processProfileId: 'p1' }),
        );
      }
      return Promise.reject(new Error(`Unexpected fetch: ${url}`));
    });
    vi.stubGlobal('fetch', fetchMock);

    renderSelect({
      machineId: 'm1',
      machineSourceFormat: 'OrcaSlicer',
      defaultProcessProfileId: undefined,
    });

    const user = userEvent.setup();
    await user.click(screen.getByRole('combobox'));
    await user.click(
      await screen.findByRole('option', { name: '0.20mm Standard @BIQU B1' }),
    );

    await waitFor(() => {
      const pairCall = fetchMock.mock.calls.find(([reqInput]) =>
        (typeof reqInput === 'string'
          ? reqInput
          : reqInput.toString()
        ).includes('/default-process-profile'),
      );
      expect(pairCall).toBeDefined();
    });

    expect(
      await screen.findByText('Process profile paired.'),
    ).toBeInTheDocument();
  });
});
