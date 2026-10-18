import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Toaster } from '@/components/ui/sonner';
import { ManageSlotDialog } from './ManageSlotDialog';

const SPOOLS = [
  {
    id: 'spool-1',
    filamentVariantId: 'v1',
    vendorName: 'Bambu Lab',
    material: 'PLA Basic',
    colorName: 'Black',
    colorHex: '#000000',
    spoolWeightGrams: 1000,
    remainingWeightGrams: 400,
    state: 'InStorage',
  },
  {
    id: 'spool-2',
    filamentVariantId: 'v2',
    vendorName: 'Polymaker',
    material: 'PETG',
    colorName: 'Red',
    colorHex: '#ff0000',
    spoolWeightGrams: 1000,
    remainingWeightGrams: 90,
    state: 'InStorage',
  },
  {
    id: 'spool-3',
    filamentVariantId: 'v3',
    vendorName: 'Bambu Lab',
    material: 'PLA Basic',
    colorName: 'White',
    colorHex: '#ffffff',
    spoolWeightGrams: 1000,
    remainingWeightGrams: 300,
    state: 'Using',
  },
];
const FILAMENTS = [
  {
    id: 'f1',
    vendorName: 'Bambu Lab',
    material: 'PLA Basic',
    materialType: 'PLA',
    variants: [
      { id: 'v1', colorName: 'Black', colorHex: '#000000' },
      { id: 'v3', colorName: 'White', colorHex: '#ffffff' },
    ],
  },
  {
    id: 'f2',
    vendorName: 'Polymaker',
    material: 'PETG',
    materialType: 'PETG',
    variants: [{ id: 'v2', colorName: 'Red', colorHex: '#ff0000' }],
  },
];

interface Options {
  /** Spool currently in the slot, if any. */
  loaded?: boolean;
  reloaderFeeding?: boolean;
  putStatus?: number;
  /** Pretend more spools match than the page returned. */
  totalCount?: number;
}

function stubFetch(
  onPut: (url: string, body: unknown) => void,
  {
    loaded = false,
    reloaderFeeding = false,
    putStatus = 200,
    totalCount,
  }: Options = {},
) {
  let slotLoaded = loaded;
  vi.stubGlobal(
    'fetch',
    vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = input.toString();
      const json = (body: unknown, status = 200) =>
        Promise.resolve(
          new Response(JSON.stringify(body), {
            status,
            headers: { 'Content-Type': 'application/json' },
          }),
        );
      if (init?.method === 'PUT') {
        onPut(url, JSON.parse(init.body as string));
        if (putStatus !== 200)
          return json({ detail: 'Slot is occupied.' }, putStatus);
        if (url.includes('/unload')) slotLoaded = false;
        return json({ id: 'spool-1', state: 'Using' });
      }
      if (url.includes('/feed-units'))
        return json({
          feedUnits: [
            {
              id: 'fu-1',
              kind: reloaderFeeding
                ? 'AutomaticFilamentReloader'
                : 'MultiMaterialUnit',
              displayLabel: 'AMS 1',
              feedingSlotId: reloaderFeeding ? 'slot-1' : null,
              feedSlots: [
                {
                  id: 'slot-1',
                  slotPosition: 0,
                  displayLabel: 'Slot 1',
                  currentSpool: slotLoaded
                    ? {
                        id: 'spool-3',
                        filamentVariantId: 'v3',
                        remainingWeightGrams: 300,
                        spoolWeightGrams: 1000,
                      }
                    : null,
                },
              ],
            },
          ],
        });
      if (url.includes('/api/v1/spools')) {
        // The server owns state, search and paging; mirror that here.
        const params = new URL(url, 'http://localhost').searchParams;
        const terms = (params.get('search') ?? '')
          .toLowerCase()
          .split(/\s+/)
          .filter(Boolean);
        const spools = SPOOLS.filter(
          (spool) =>
            (params.get('state') === null ||
              ['0', 'InStorage'].includes(params.get('state') ?? '') ===
                (spool.state === 'InStorage')) &&
            terms.every((term) =>
              [spool.vendorName, spool.material, spool.colorName]
                .join(' ')
                .toLowerCase()
                .includes(term),
            ),
        );
        return json({ spools, totalCount: totalCount ?? spools.length });
      }
      if (url.includes('/api/v1/filaments'))
        return json({ filaments: FILAMENTS });
      return json({});
    }),
  );
}

function renderDialog(
  onPut: (url: string, body: unknown) => void = () => {},
  options?: Options,
) {
  stubFetch(onPut, options);
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <ManageSlotDialog
        printerId="printer-1"
        feedSlotId="slot-1"
        slotLabel="AMS 1 · Slot 1"
        open
        onOpenChange={() => {}}
      />
      <Toaster />
    </QueryClientProvider>,
  );
}

/** Spool rows only; the material filter chips are radios too. */
function spoolRows() {
  return within(
    screen.getByRole('group', { name: 'Spools in storage' }),
  ).getAllByRole('radio');
}

describe('ManageSlotDialog', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  describe('empty slot', () => {
    it('asks the server for the first page of spools in storage and lists them', async () => {
      renderDialog();

      expect(
        await screen.findByRole('radio', { name: /Bambu Lab PLA Basic/ }),
      ).toBeInTheDocument();
      expect(
        screen.getByRole('radio', { name: /Polymaker PETG/ }),
      ).toBeInTheDocument();
      expect(spoolRows()).toHaveLength(2);
      expect(screen.getByText('400 g')).toBeInTheDocument();

      const listCall = vi
        .mocked(fetch)
        .mock.calls.map(([input]) => new URL(String(input), 'http://localhost'))
        .find((url) => url.pathname === '/api/v1/spools');
      expect(listCall?.searchParams.get('state')).toBe('0');
      expect(listCall?.searchParams.get('page')).toBe('1');
      expect(listCall?.searchParams.get('pageSize')).toBe('50');
      expect(listCall?.searchParams.has('search')).toBe(false);
    });

    it('filters the loaded spools by material', async () => {
      const user = userEvent.setup();
      renderDialog();
      await screen.findByRole('radio', { name: /Polymaker PETG/ });

      const filters = screen.getByRole('radiogroup', {
        name: 'Filter spools by material',
      });
      expect(within(filters).getByRole('radio', { name: /All\s*2/ }));
      await user.click(within(filters).getByRole('radio', { name: /PETG/ }));
      expect(spoolRows()).toHaveLength(1);
      expect(
        screen.queryByRole('radio', { name: /Bambu Lab PLA Basic/ }),
      ).not.toBeInTheDocument();
    });

    it('sends the search to the server and clears filters', async () => {
      const user = userEvent.setup();
      renderDialog();
      await screen.findByRole('radio', { name: /Polymaker PETG/ });

      const box = screen.getByRole('textbox', { name: 'Search spools' });
      await user.type(box, 'poly');
      await waitFor(() => expect(spoolRows()).toHaveLength(1));
      expect(spoolRows()[0]).toHaveAccessibleName(/Polymaker PETG/);
      expect(
        vi
          .mocked(fetch)
          .mock.calls.some(([input]) =>
            new URL(String(input), 'http://localhost').searchParams.has(
              'search',
              'poly',
            ),
          ),
      ).toBe(true);

      await user.clear(box);
      await user.type(box, 'zzz');
      expect(await screen.findByText('No spools match')).toBeInTheDocument();
      // The search box stays, so the operator can edit the text.
      expect(
        screen.getByRole('textbox', { name: 'Search spools' }),
      ).toHaveValue('zzz');

      await user.click(screen.getByRole('button', { name: 'Clear filters' }));
      expect(
        screen.getByRole('textbox', { name: 'Search spools' }),
      ).toHaveValue('');
      await waitFor(() => expect(spoolRows()).toHaveLength(2));
    });

    it('says when more spools match than were loaded, and drops the counts', async () => {
      renderDialog(() => {}, { totalCount: 120 });

      expect(
        await screen.findByText('Showing 2 of 120. Search to narrow the list.'),
      ).toBeInTheDocument();
      const filters = screen.getByRole('radiogroup', {
        name: 'Filter spools by material',
      });
      expect(
        within(filters).getByRole('radio', { name: 'All' }),
      ).toBeInTheDocument();
    });

    it('loads the chosen spool into this slot', async () => {
      const user = userEvent.setup();
      let putUrl = '';
      let putBody: unknown;
      renderDialog((url, body) => {
        putUrl = url;
        putBody = body;
      });

      const load = await screen.findByRole('button', { name: 'Load spool' });
      expect(load).toBeDisabled();
      await user.click(
        await screen.findByRole('radio', { name: /Polymaker PETG/ }),
      );
      await user.click(load);

      await waitFor(() => expect(putBody).toEqual({ feedSlotId: 'slot-1' }));
      expect(putUrl).toContain('/api/v1/spools/spool-2/load');
    });

    it('shows the server reason when loading fails', async () => {
      const user = userEvent.setup();
      renderDialog(() => {}, { putStatus: 409 });

      await user.click(
        await screen.findByRole('radio', { name: /Polymaker PETG/ }),
      );
      await user.click(screen.getByRole('button', { name: 'Load spool' }));

      expect(await screen.findByText('Slot is occupied.')).toBeInTheDocument();
    });
  });

  describe('loaded slot', () => {
    it('shows the current spool and unloads it back to storage, then offers the picker', async () => {
      const user = userEvent.setup();
      let putUrl = '';
      let putBody: unknown;
      renderDialog(
        (url, body) => {
          putUrl = url;
          putBody = body;
        },
        { loaded: true },
      );

      expect(
        await screen.findByText('Bambu Lab PLA Basic'),
      ).toBeInTheDocument();
      expect(screen.getByText('White')).toBeInTheDocument();
      expect(screen.getByText('300 g of 1000 g')).toBeInTheDocument();
      expect(
        screen.queryByText('Unloading clears the feeding slot.'),
      ).not.toBeInTheDocument();

      await user.click(
        screen.getByRole('button', { name: 'Return to storage' }),
      );

      await waitFor(() =>
        expect(putBody).toEqual({ action: 'ReturnToStorage' }),
      );
      expect(putUrl).toContain('/api/v1/spools/spool-3/unload');
      // The slot refetches empty, so the dialog switches to the picker by itself.
      expect(
        await screen.findByText('Empty. Choose a spool to load.'),
      ).toBeInTheDocument();
      expect(
        await screen.findByRole('button', { name: 'Load spool' }),
      ).toBeInTheDocument();
    });

    it('marks the spool finished', async () => {
      const user = userEvent.setup();
      let putBody: unknown;
      renderDialog(
        (_url, body) => {
          putBody = body;
        },
        { loaded: true },
      );

      await user.click(
        await screen.findByRole('button', { name: 'Mark finished' }),
      );

      await waitFor(() => expect(putBody).toEqual({ action: 'MarkFinished' }));
    });

    it('warns that unloading a reloader feeding slot clears the selection', async () => {
      renderDialog(() => {}, { loaded: true, reloaderFeeding: true });

      expect(
        await screen.findByText('Unloading clears the feeding slot.'),
      ).toBeInTheDocument();
    });
  });
});
