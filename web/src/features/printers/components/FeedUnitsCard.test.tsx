import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { FeedUnitResponse } from '@/lib/api/generated/endpoints.schemas';
import { FeedUnitsCard, type FeedUnitsCardProps } from './FeedUnitsCard';

function stubFetch(
  onDelete?: (url: string) => void,
  feedUnits?: FeedUnitResponse[],
) {
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
              feedUnits: feedUnits ?? [
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
                      displayLabel: 'AMS 1 · Slot 1',
                      currentSpool: {
                        id: 'spool-1',
                        filamentVariantId: 'var-1',
                        remainingWeightGrams: 400,
                      },
                    },
                    {
                      id: 'slot-2',
                      slotPosition: 1,
                      displayLabel: 'AMS 1 · Slot 2',
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
                      displayLabel: 'Splicer · Slot 1',
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
  feedUnits?: FeedUnitResponse[],
) {
  stubFetch(onDelete, feedUnits);
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
    expect(screen.getByText('400 g')).toBeInTheDocument();
  });

  it('shows no spool for a Feed slot with no telemetry', async () => {
    renderCard();

    expect(await screen.findAllByText('No spool')).not.toHaveLength(0);
    expect(screen.queryByText('Empty')).not.toBeInTheDocument();
  });

  it('shows grams, one mismatch tag and the active mark in one slot', async () => {
    renderCard(undefined, {
      feedReadings: [
        {
          kind: 'MultiMaterialUnit',
          unitIndex: 0,
          slots: [
            {
              slotPosition: 0,
              materialType: 'PETG',
              subBrand: 'Basic',
              remainPercent: 0,
              active: true,
            },
            { slotPosition: 1, empty: true },
          ],
        },
      ],
    });
    await screen.findByText('Bambu Lab PLA Basic');
    const row = screen.getByText('400 g').closest('li');
    if (!row) throw new Error('Expected a registered spool row');
    // One number per source: recorded grams win over the printer percentage.
    expect(within(row).queryByText('0%')).not.toBeInTheDocument();
    expect(within(row).getByText('Detected: PETG Basic')).toBeInTheDocument();
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
          feedReadings={[
            {
              kind: 'MultiMaterialUnit',
              unitIndex: 0,
              slots: [
                {
                  slotPosition: 0,
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
    await screen.findByText('Bambu Lab PLA Basic');
    expect(screen.getByText('Active')).toBeInTheDocument();
    rerender(card(true, 61));
    expect(screen.getByText('Active')).toBeInTheDocument();
    expect(screen.queryByText('Offline · last known')).not.toBeInTheDocument();
    rerender(card(false, 61));
    expect(screen.queryByText('Active')).not.toBeInTheDocument();
    expect(screen.getByText('Offline · last known')).toBeInTheDocument();
  });

  it('shows detected but unregistered filament without hiding the inventory state', async () => {
    renderCard(undefined, {
      feedReadings: [
        {
          kind: 'MultiMaterialUnit',
          unitIndex: 0,
          slots: [{ slotPosition: 1, materialType: 'PETG', subBrand: 'Basic' }],
        },
      ],
    });
    expect(await screen.findByText('PETG Basic')).toBeInTheDocument();
    expect(screen.getByText('Unregistered')).toBeInTheDocument();
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
    expect(screen.getByText('0 g')).toBeInTheDocument();
    expect(screen.getByText('Inactive')).toBeInTheDocument();
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
    expect(screen.getByRole('status')).toHaveTextContent('Loading feed units');
    finish(new Response('{}', { status: 500 }));
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Could not load feed units',
    );
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
    expect(screen.queryByText('No feed units')).not.toBeInTheDocument();
    await userEvent
      .setup()
      .click(screen.getByRole('button', { name: 'Retry' }));
    finish(
      new Response(JSON.stringify({ feedUnits: [] }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );
    expect(await screen.findByText('No feed units')).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('opens the slot dialog from the row, with Load spool shown on empty slots', async () => {
    const user = userEvent.setup();
    renderCard();

    await screen.findByText('AMS 1');
    const occupied = screen.getByRole('listitem', { name: 'AMS 1 · Slot 1' });
    expect(within(occupied).queryByText('Load spool')).not.toBeInTheDocument();
    // AMS slot 2 and the reloader slot are empty.
    expect(screen.getAllByText('Load spool')).toHaveLength(2);

    await user.click(
      within(occupied).getByRole('button', { name: /AMS 1 · Slot 1/ }),
    );
    const dialog = await screen.findByRole('dialog');
    expect(dialog).toHaveTextContent('AMS 1 · Slot 1');
    expect(dialog).not.toHaveTextContent('AMS 1 · AMS 1');
  });

  it('has an Add feed unit action', async () => {
    renderCard();

    expect(
      await screen.findByRole('button', { name: 'Add feed unit' }),
    ).toBeInTheDocument();
  });

  it('offers unit actions only for the Automatic filament reloader, not the connector-derived MMU', async () => {
    renderCard();

    await screen.findByText('AMS 1');
    expect(screen.getByText('Splicer')).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: 'Actions for Splicer' }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Actions for AMS 1' }),
    ).not.toBeInTheDocument();
  });

  it('removes a Feed unit after confirming', async () => {
    const user = userEvent.setup();
    let deletedUrl: string | undefined;
    renderCard((url) => {
      deletedUrl = url;
    });

    await user.click(
      await screen.findByRole('button', { name: 'Actions for Splicer' }),
    );
    await user.click(await screen.findByRole('menuitem', { name: 'Remove' }));
    expect(deletedUrl).toBeUndefined();
    await user.click(
      within(await screen.findByRole('alertdialog')).getByRole('button', {
        name: 'Remove',
      }),
    );

    await waitFor(() =>
      expect(deletedUrl).toContain('/api/v1/feed-units/fu-2'),
    );
  });

  it.each([undefined, null])(
    'hides Remove for a lone external spool with replacement %s',
    async (supersededByFeedUnitId) => {
      renderCard(undefined, {}, [
        {
          id: 'external-1',
          kind: 'ExternalSpool',
          displayLabel: 'External spool',
          supersededByFeedUnitId,
          feedSlots: [],
        },
      ]);

      await screen.findByText('External spool');
      expect(
        screen.queryByRole('button', { name: 'Actions for External spool' }),
      ).not.toBeInTheDocument();
    },
  );

  it('hides Remove for an external spool without a replacement alongside other units', async () => {
    renderCard(undefined, {}, [
      {
        id: 'external-1',
        kind: 'ExternalSpool',
        displayLabel: 'External spool',
        supersededByFeedUnitId: null,
        feedSlots: [],
      },
      {
        id: 'reloader-1',
        kind: 'AutomaticFilamentReloader',
        displayLabel: 'Splicer',
        feedSlots: [],
      },
    ]);

    await screen.findByText('External spool');
    expect(
      screen.queryByRole('button', { name: 'Actions for External spool' }),
    ).not.toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: 'Actions for Splicer' }),
    ).toBeInTheDocument();
  });

  it('removes a superseded external spool through its endpoint', async () => {
    const user = userEvent.setup();
    let deletedUrl: string | undefined;
    renderCard(
      (url) => {
        deletedUrl = url;
      },
      {},
      [
        {
          id: 'external-1',
          kind: 'ExternalSpool',
          displayLabel: 'External spool',
          supersededByFeedUnitId: 'reloader-1',
          feedSlots: [],
        },
      ],
    );

    await user.click(
      await screen.findByRole('button', { name: 'Actions for External spool' }),
    );
    await user.click(await screen.findByRole('menuitem', { name: 'Remove' }));
    await user.click(
      within(await screen.findByRole('alertdialog')).getByRole('button', {
        name: 'Remove',
      }),
    );
    await waitFor(() =>
      expect(deletedUrl).toContain('/api/v1/feed-units/external-1'),
    );
  });
});
