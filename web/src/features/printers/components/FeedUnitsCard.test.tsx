import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { FeedUnitsCard } from './FeedUnitsCard';

function stubFetch() {
  vi.stubGlobal(
    'fetch',
    vi.fn((input: RequestInfo | URL) => {
      const url = input.toString();
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

function renderCard() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <FeedUnitsCard printerId="printer-1" />
    </QueryClientProvider>,
  );
}

describe('FeedUnitsCard', () => {
  beforeEach(() => {
    stubFetch();
  });

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

    expect(await screen.findByText('Empty')).toBeInTheDocument();
  });
});
