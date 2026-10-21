import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { describe, expect, it, vi } from 'vitest';
import { RunSlicerReadiness } from './RunSlicerReadiness';

vi.mock('@/lib/api/generated/parts/parts', () => {
  const slicers: Record<string, { engine: string; version: string } | null> = {
    a: { engine: 'OrcaSlicer', version: '2.4.2' },
    b: { engine: 'OrcaSlicer', version: '2.4.2' },
    c: { engine: 'BambuStudio', version: '2.7.1' },
    pre: null,
  };
  return {
    getOpenFarmApiEndpointsPartsGetPartEndpointQueryOptions: (id: string) => ({
      queryKey: ['part', id],
      queryFn: async () => ({ data: { slicer: slicers[id] } }),
    }),
  };
});
vi.mock('@/lib/api/generated/slicing/slicing', () => ({
  useOpenFarmApiEndpointsSlicingGetSlicingReadinessEndpoint: (params: {
    engine: string;
  }) => ({
    isLoading: false,
    data: {
      data: {
        printers: [
          {
            printerId: 'p1',
            printerName: 'P1S Lab',
            issues:
              params.engine === 'BambuStudio'
                ? [
                    {
                      kind: 'Process',
                      code: 'Missing',
                      message: 'Process profile is not in Bambu Studio 2.7.1.',
                      link: '/printers/p1/edit',
                    },
                  ]
                : [],
          },
        ],
        filaments: [],
      },
    },
  }),
}));

function renderRun(partIds: string[]) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  const view = render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <RunSlicerReadiness partIds={partIds} />
      </MemoryRouter>
    </QueryClientProvider>,
  );
  return { ...view, queryClient };
}

describe('Run slicer readiness', () => {
  it('shows one readiness block per distinct slicer the parts are pinned to', async () => {
    renderRun(['a', 'b', 'c', 'pre']);

    expect(
      await screen.findByText('Parts pinned to OrcaSlicer 2.4.2'),
    ).toBeInTheDocument();
    expect(
      screen.getByText('Parts pinned to Bambu Studio 2.7.1'),
    ).toBeInTheDocument();
    expect(screen.getAllByText(/^Parts pinned to /)).toHaveLength(2);
    expect(
      screen.getByText('Production is blocked with Bambu Studio 2.7.1'),
    ).toBeInTheDocument();
    expect(
      screen.getByText(
        'Every printer and filament resolves in OrcaSlicer 2.4.2.',
      ),
    ).toBeInTheDocument();
  });

  it('shows nothing when no part is pinned to a slicer', async () => {
    const { container, queryClient } = renderRun(['pre']);

    await waitFor(() => expect(queryClient.isFetching()).toBe(0));
    expect(container).toBeEmptyDOMElement();
  });
});
