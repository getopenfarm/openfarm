import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { FeedUnitsCard } from './FeedUnitsCard';

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

function renderCard(onDelete?: (url: string) => void) {
  stubFetch(onDelete);
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <FeedUnitsCard printerId="printer-1" />
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

  it('shows Empty for a Feed Slot with nothing loaded', async () => {
    renderCard();

    expect(await screen.findAllByText('Empty')).not.toHaveLength(0);
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
