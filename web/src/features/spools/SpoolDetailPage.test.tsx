import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Toaster } from '@/components/ui/sonner';
import { SpoolDetailPage } from './SpoolDetailPage';

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

const spool = {
  id: 'spool-1',
  filamentId: 'f-1',
  label: 'Shelf A',
  colorName: 'Jade White',
  material: 'PLA',
  vendorName: 'Bambu',
  spoolWeightGrams: 1000,
  remainingWeightGrams: 800,
  state: 'Using',
  feedSlotLabel: 'AMS 1 · A1',
};

/** What the page sends for the purchase fields when a spool has none. */
const NO_PURCHASE_INFO = {
  price: null,
  supplier: null,
  orderNumber: null,
  receivedOn: null,
};

interface FakeApi {
  /** Answers the PUT; defaults to echoing the saved label and weight. */
  put?: (body: Record<string, unknown>) => Response;
  /** Answers GET /api/v1/spools/spool-1; defaults to the spool. */
  spool?: () => Response;
}

/** Routes the page's three calls: the spool, its history (always empty here) and the save. */
function fakeApi({ put, spool: getSpool }: FakeApi = {}) {
  return vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
    if (init?.method === 'PUT') {
      const body = JSON.parse(init.body as string);
      return Promise.resolve(
        put?.(body) ??
          jsonResponse({
            ...spool,
            label: body.label,
            remainingWeightGrams: body.remainingWeightGrams,
          }),
      );
    }
    if (input.toString().endsWith('/history')) {
      return Promise.resolve(jsonResponse({ entries: [], totalCount: 0 }));
    }
    return Promise.resolve(getSpool?.() ?? jsonResponse(spool));
  });
}

function renderPage(fetchMock: ReturnType<typeof vi.fn>) {
  vi.stubGlobal('fetch', fetchMock);
  const router = createMemoryRouter(
    [
      { path: '/filament/spools/:id', element: <SpoolDetailPage /> },
      { path: '/filament/:id', element: <div>Filament page</div> },
      { path: '/filament', element: <div>Filament list page</div> },
    ],
    { initialEntries: ['/filament/spools/spool-1'] },
  );
  render(
    <QueryClientProvider
      client={
        new QueryClient({
          defaultOptions: {
            queries: { retry: false },
            mutations: { retry: false },
          },
        })
      }
    >
      <RouterProvider router={router} />
      <Toaster />
    </QueryClientProvider>,
  );
}

function putBodies(fetchMock: ReturnType<typeof vi.fn>) {
  return fetchMock.mock.calls
    .filter(([, init]) => (init as RequestInit | undefined)?.method === 'PUT')
    .map(([, init]) => JSON.parse((init as RequestInit).body as string));
}

afterEach(() => vi.unstubAllGlobals());

describe('SpoolDetailPage', () => {
  it("links back to its filament's spools", async () => {
    renderPage(fakeApi());

    // Before the spool loads, the link can only go to the filament list.
    await screen.findByRole('heading', { name: 'Shelf A' });
    expect(
      screen.getByRole('link', { name: 'Back to spools' }),
    ).toHaveAttribute('href', '/filament/f-1?tab=spools');
  });

  it('loads the one spool by id and saves label and remaining weight in one PUT', async () => {
    const user = userEvent.setup();
    const fetchMock = fakeApi();
    renderPage(fetchMock);

    expect(
      await screen.findByRole('heading', { name: 'Shelf A' }),
    ).toBeInTheDocument();
    expect(screen.getByText('In use')).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: 'Record usage' }),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Unload' })).toBeInTheDocument();
    const save = screen.getByRole('button', { name: 'Save' });
    expect(save).toBeDisabled();

    const weight = screen.getByLabelText('Remaining weight');
    await user.clear(weight);
    await user.type(weight, '1200');
    await user.click(save);
    expect(
      await screen.findByText('Cannot exceed the spool capacity (1000 g).'),
    ).toBeInTheDocument();

    await user.clear(screen.getByLabelText('Label'));
    await user.clear(weight);
    await user.type(weight, '650');
    await user.click(save);

    expect(await screen.findByText('Spool saved.')).toBeInTheDocument();
    const urls = fetchMock.mock.calls.map(([input]) => input.toString());
    expect(urls.some((url) => url.endsWith('/api/v1/spools/spool-1'))).toBe(
      true,
    );
    expect(urls.some((url) => url.endsWith('/api/v1/spools'))).toBe(false);
    expect(putBodies(fetchMock)).toEqual([
      {
        label: null,
        remainingWeightGrams: 650,
        reason: null,
        ...NO_PURCHASE_INFO,
      },
    ]);
  });

  it('asks for a reason only once the weight changes, and sends it with the save', async () => {
    const user = userEvent.setup();
    const fetchMock = fakeApi();
    renderPage(fetchMock);
    await screen.findByRole('heading', { name: 'Shelf A' });

    expect(
      screen.queryByLabelText('Reason (optional)'),
    ).not.toBeInTheDocument();
    await user.type(screen.getByLabelText('Label'), ' 2');
    expect(
      screen.queryByLabelText('Reason (optional)'),
    ).not.toBeInTheDocument();

    const weight = screen.getByLabelText('Remaining weight');
    await user.clear(weight);
    await user.type(weight, '750');
    await user.type(
      await screen.findByLabelText('Reason (optional)'),
      'weighed on scale',
    );
    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByText('Spool saved.')).toBeInTheDocument();
    expect(putBodies(fetchMock)).toEqual([
      {
        label: 'Shelf A 2',
        remainingWeightGrams: 750,
        reason: 'weighed on scale',
        ...NO_PURCHASE_INFO,
      },
    ]);
  });

  it('does not send a reason when only the label changes', async () => {
    const user = userEvent.setup();
    const fetchMock = fakeApi();
    renderPage(fetchMock);
    await user.type(await screen.findByLabelText('Label'), 'x');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByText('Spool saved.')).toBeInTheDocument();
    expect(putBodies(fetchMock)[0].reason).toBeNull();
  });

  it('shows the server problem detail when the save is rejected', async () => {
    const user = userEvent.setup();
    renderPage(
      fakeApi({
        put: () => jsonResponse({ detail: 'Weight exceeds capacity.' }, 422),
      }),
    );
    await user.type(await screen.findByLabelText('Label'), 'x');
    await user.click(screen.getByRole('button', { name: 'Save' }));
    expect(
      await screen.findByText('Weight exceeds capacity.'),
    ).toBeInTheDocument();
  });

  it('shows not found for an unknown spool', async () => {
    renderPage(fakeApi({ spool: () => new Response(null, { status: 404 }) }));
    expect(await screen.findByText('Spool not found')).toBeInTheDocument();
  });

  it('shows a retryable error when the spool cannot be loaded', async () => {
    renderPage(fakeApi({ spool: () => new Response(null, { status: 500 }) }));
    expect(await screen.findByText('Could not load spool')).toBeInTheDocument();
  });

  describe('purchase info', () => {
    const pricedSpool = {
      ...spool,
      price: 18.5,
      pricePerKg: 18.5,
      supplier: 'Filament Shop',
      orderNumber: 'PO-1042',
      receivedOn: '2026-10-20',
    };

    it('shows what was paid, the supplier, the order and the date, with the price per kg', async () => {
      renderPage(fakeApi({ spool: () => jsonResponse(pricedSpool) }));

      expect(await screen.findByLabelText('Price per spool')).toHaveValue(18.5);
      expect(screen.getByLabelText('Supplier')).toHaveValue('Filament Shop');
      expect(screen.getByLabelText('Order number')).toHaveValue('PO-1042');
      expect(screen.getByLabelText('Received on')).toHaveValue('2026-10-20');
      expect(screen.getByText('USD 18.50 per kg')).toBeInTheDocument();
    });

    it('saves a changed supplier with the label and weight in one PUT', async () => {
      const user = userEvent.setup();
      const fetchMock = fakeApi({ spool: () => jsonResponse(pricedSpool) });
      renderPage(fetchMock);

      const supplier = await screen.findByLabelText('Supplier');
      await user.clear(supplier);
      await user.type(supplier, 'Other Shop');
      await user.click(screen.getByRole('button', { name: 'Save' }));

      expect(await screen.findByText('Spool saved.')).toBeInTheDocument();
      expect(putBodies(fetchMock)).toEqual([
        {
          label: 'Shelf A',
          remainingWeightGrams: 800,
          reason: null,
          price: 18.5,
          supplier: 'Other Shop',
          orderNumber: 'PO-1042',
          receivedOn: '2026-10-20',
        },
      ]);
    });

    it('clears the purchase info when the fields are emptied', async () => {
      const user = userEvent.setup();
      const fetchMock = fakeApi({ spool: () => jsonResponse(pricedSpool) });
      renderPage(fetchMock);

      await user.clear(await screen.findByLabelText('Price per spool'));
      await user.clear(screen.getByLabelText('Supplier'));
      await user.clear(screen.getByLabelText('Order number'));
      fireEvent.change(screen.getByLabelText('Received on'), {
        target: { value: '' },
      });
      await user.click(screen.getByRole('button', { name: 'Save' }));

      expect(await screen.findByText('Spool saved.')).toBeInTheDocument();
      expect(putBodies(fetchMock)[0]).toMatchObject(NO_PURCHASE_INFO);
    });

    it('keeps a price of 0 as 0, and refuses a negative price', async () => {
      const user = userEvent.setup();
      const fetchMock = fakeApi({ spool: () => jsonResponse(pricedSpool) });
      renderPage(fetchMock);

      const price = await screen.findByLabelText('Price per spool');
      await user.clear(price);
      await user.type(price, '-1');
      await user.click(screen.getByRole('button', { name: 'Save' }));
      expect(await screen.findByText('Must be 0 or more')).toBeInTheDocument();
      expect(putBodies(fetchMock)).toHaveLength(0);

      await user.clear(price);
      await user.type(price, '0');
      await user.click(screen.getByRole('button', { name: 'Save' }));
      expect(await screen.findByText('Spool saved.')).toBeInTheDocument();
      expect(putBodies(fetchMock)[0].price).toBe(0);
    });
  });
  it('finishes the spool from its page, writing off what is left', async () => {
    const user = userEvent.setup();
    const fetchMock = fakeApi();
    renderPage(fetchMock);

    await user.click(await screen.findByRole('button', { name: 'Finish' }));
    const dialog = screen.getByRole('alertdialog');
    expect(dialog).toHaveTextContent('Finish Shelf A?');
    expect(dialog).toHaveTextContent('Writes off 800 g.');
    await user.type(within(dialog).getByRole('textbox'), 'tangled');
    await user.click(within(dialog).getByRole('button', { name: 'Finish' }));

    expect(await screen.findByText('Spool finished.')).toBeInTheDocument();
    expect(putBodies(fetchMock)).toEqual([
      { action: 'MarkFinished', reason: 'tangled' },
    ]);
  });

  it('restores a finished spool instead of offering load or finish', async () => {
    const user = userEvent.setup();
    const fetchMock = fakeApi({
      spool: () =>
        jsonResponse({
          ...spool,
          state: 'Finished',
          remainingWeightGrams: 0,
          feedSlotLabel: null,
        }),
    });
    renderPage(fetchMock);

    await user.click(await screen.findByRole('button', { name: 'Restore' }));

    expect(await screen.findByText('Spool restored.')).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Finish' }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Load' }),
    ).not.toBeInTheDocument();
    expect(
      fetchMock.mock.calls.some(
        ([input, init]) =>
          String(input).endsWith('/api/v1/spools/spool-1/restore') &&
          (init as RequestInit | undefined)?.method === 'PUT',
      ),
    ).toBe(true);
  });
});
