import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { FeedUnitsCard, type FeedUnitsCardProps } from './FeedUnitsCard';

function stubFetch(onDelete?: (url: string) => void) {
  vi.stubGlobal(
    'fetch',
    vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = input.toString();
      if (init?.method === 'DELETE') {
        onDelete?.(url);
        return Promise.resolve(
          new Response(null, { status: 204, headers: {} }),
        );
      }
      if (url.includes('/feed-units')) {
        return Promise.resolve(
          new Response(
            JSON.stringify({
              feedUnits: [
                {
                  id: 'fu-1',
                  kind: 'MultiMaterialUnit',
                  unitIndex: 0,
                  displayLabel: 'AMS 1',
                  isRevealed: true,
                  feedSlots: [
                    {
                      id: 'slot-1',
                      slotPosition: 0,
                      displayLabel: 'Slot 1',
                      currentSpool: {
                        id: 'spool-1',
                        filamentVariantId: 'var-1',
                        remainingWeightGrams: 400,
                      },
                    },
                    {
                      id: 'slot-2',
                      slotPosition: 1,
                      displayLabel: 'Slot 2',
                      currentSpool: null,
                    },
                  ],
                },
                {
                  id: 'fu-2',
                  kind: 'AutomaticFilamentReloader',
                  displayLabel: 'Splicer',
                  isRevealed: true,
                  feedSlots: [
                    {
                      id: 'slot-3',
                      slotPosition: 0,
                      displayLabel: 'Slot 1',
                      currentSpool: null,
                    },
                  ],
                },
              ],
            }),
            { status: 200, headers: { 'Content-Type': 'application/json' } },
          ),
        );
      }
      if (url.includes('/api/v1/filaments')) {
        return Promise.resolve(
          new Response(
            JSON.stringify({
              filaments: [
                {
                  id: 'f-1',
                  vendorName: 'Bambu Lab',
                  material: 'PLA Basic',
                  materialType: 'PLA',
                  variants: [
                    { id: 'var-1', colorName: 'Black', colorHex: '#18181b' },
                  ],
                },
              ],
            }),
            { status: 200, headers: { 'Content-Type': 'application/json' } },
          ),
        );
      }
      return Promise.resolve(
        new Response(JSON.stringify({}), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }),
      );
    }),
  );
}

function renderCard(
  onDelete?: (url: string) => void,
  props: Partial<FeedUnitsCardProps> = {},
) {
  stubFetch(onDelete);
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <FeedUnitsCard printerId="printer-1" isReachable {...props} />
    </QueryClientProvider>,
  );
}

describe('FeedUnitsCard', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('shows a loaded spool resolved against the filament catalog', async () => {
    renderCard();

    expect(await screen.findByText('Bambu Lab PLA Basic')).toBeInTheDocument();
    expect(screen.getByText('Black')).toBeInTheDocument();
    expect(screen.getByText('Tracked: 400 g')).toBeInTheDocument();
  });

  it('shows unknown detection for a Feed Slot with no telemetry', async () => {
    renderCard();

    expect(await screen.findAllByText('No registered spool')).not.toHaveLength(
      0,
    );
    expect(screen.queryByText('Empty')).not.toBeInTheDocument();
  });

  it('combines tracked grams, detected contents and live percentage in one slot', async () => {
    renderCard(undefined, {
      amsUnits: [
        {
          unitIndex: 0,
          trays: [
            {
              slotIndex: 0,
              materialType: 'PETG',
              subBrand: 'Basic',
              remainPercent: 0,
              active: true,
            },
            { slotIndex: 1, empty: true },
          ],
        },
      ],
    });
    await screen.findByText('Bambu Lab PLA Basic');
    const row = screen.getByText('Tracked: 400 g').closest('li');
    if (!row) throw new Error('Expected a registered spool row');
    expect(within(row).getByText('Printer estimate: 0%')).toBeInTheDocument();
    expect(
      within(row).getByText('Printer detected: PETG · Basic'),
    ).toBeInTheDocument();
    expect(within(row).getByText('Active')).toBeInTheDocument();
    expect(screen.getByText('Empty')).toBeInTheDocument();
  });

  it('updates live values and marks them last known when offline', async () => {
    stubFetch();
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });
    const card = (isReachable: boolean, remainPercent: number) => (
      <QueryClientProvider client={queryClient}>
        <FeedUnitsCard
          printerId="printer-1"
          isReachable={isReachable}
          amsUnits={[
            {
              unitIndex: 0,
              trays: [
                {
                  slotIndex: 0,
                  materialType: 'PLA',
                  active: true,
                  remainPercent,
                },
              ],
            },
          ]}
        />
      </QueryClientProvider>
    );
    const { rerender } = render(card(true, 62));
    await screen.findByText('Printer estimate: 62%');
    rerender(card(true, 61));
    expect(screen.getByText('Printer estimate: 61%')).toBeInTheDocument();
    rerender(card(false, 61));
    expect(screen.queryByText('Active')).not.toBeInTheDocument();
    expect(
      screen.getByText('Printer offline · Detection is last known'),
    ).toBeInTheDocument();
  });

  it('shows detected but unregistered filament without hiding the inventory state', async () => {
    renderCard(undefined, {
      amsUnits: [
        {
          unitIndex: 0,
          trays: [{ slotIndex: 1, materialType: 'PETG', subBrand: 'Basic' }],
        },
      ],
    });
    expect(await screen.findByText('PETG')).toBeInTheDocument();
    expect(
      screen.getByText('Detected · Unregistered · Basic'),
    ).toBeInTheDocument();
  });

  it('retains registered spool and zero grams when catalog lookup fails', async () => {
    stubFetch();
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });
    queryClient.setQueryData(['/api/v1/printers/printer-1/feed-units'], {
      data: {
        feedUnits: [
          {
            id: 'fu-zero',
            kind: 'MultiMaterialUnit',
            unitIndex: 0,
            feedSlots: [
              {
                id: 'zero',
                slotPosition: 0,
                deactivatedAtUtc: '2026-10-05',
                currentSpool: {
                  filamentVariantId: 'missing',
                  remainingWeightGrams: 0,
                },
              },
            ],
          },
        ],
      },
    });
    render(
      <QueryClientProvider client={queryClient}>
        <FeedUnitsCard printerId="printer-1" isReachable />
      </QueryClientProvider>,
    );
    expect(screen.getByText('Registered spool')).toBeInTheDocument();
    expect(screen.getByText('Tracked: 0 g')).toBeInTheDocument();
    expect(screen.getByText('Inactive slot')).toBeInTheDocument();
  });

  it('shows loading, then an error with retry, rather than a false empty state', async () => {
    let finish: (value: Response) => void = () => {};
    vi.stubGlobal(
      'fetch',
      vi.fn(
        () =>
          new Promise<Response>((resolve) => {
            finish = resolve;
          }),
      ),
    );
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });
    render(
      <QueryClientProvider client={queryClient}>
        <FeedUnitsCard printerId="printer-1" isReachable />
      </QueryClientProvider>,
    );
    expect(screen.getByRole('status')).toHaveTextContent('Loading Feed Units');
    finish(new Response('{}', { status: 500 }));
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Could not load Feed Units',
    );
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
    expect(
      screen.queryByText('No Feed Units on this printer'),
    ).not.toBeInTheDocument();
    await userEvent
      .setup()
      .click(screen.getByRole('button', { name: 'Retry' }));
    finish(
      new Response(JSON.stringify({ feedUnits: [] }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );
    expect(
      await screen.findByText('No Feed Units on this printer'),
    ).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('has an Add a Feed Unit action', async () => {
    renderCard();

    expect(
      await screen.findByRole('button', { name: 'Add a Feed Unit' }),
    ).toBeInTheDocument();
  });

  it('offers Remove only for the Automatic Filament Reloader, not the connector-derived MMU', async () => {
    renderCard();

    await screen.findByText('AMS 1');
    expect(screen.getByText('Splicer')).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: 'Remove' })).toHaveLength(1);
  });

  it('removes a Feed Unit', async () => {
    const user = userEvent.setup();
    let deletedUrl: string | undefined;
    renderCard((url) => {
      deletedUrl = url;
    });

    await user.click(await screen.findByRole('button', { name: 'Remove' }));

    await waitFor(() =>
      expect(deletedUrl).toContain('/api/v1/feed-units/fu-2'),
    );
  });
});
