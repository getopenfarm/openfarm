import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import {
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import {
  createMemoryRouter,
  Link,
  Outlet,
  RouterProvider,
  useLocation,
  useParams,
} from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Toaster } from '@/components/ui/sonner';
import { SpoolFormPage } from './SpoolFormPage';

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

// "Polymaker PETG Jade White" holds the letters m, a, t, t, e in order, which cmdk's own fuzzy search would count as a hit for "matte".
const FILAMENTS_RESPONSE = {
  filaments: [
    {
      id: 'f-1',
      vendorName: 'Bambu Lab',
      material: 'PLA Basic',
      materialType: 'PLA',
      variants: [{ id: 'var-1', colorName: 'Black', colorHex: '#18181b' }],
    },
    {
      id: 'f-2',
      vendorName: 'Bambu Lab',
      material: 'PLA Matte',
      materialType: 'PLA',
      variants: [{ id: 'var-2', colorName: 'Charcoal', colorHex: '#555555' }],
    },
    {
      id: 'f-3',
      vendorName: 'Polymaker',
      material: 'PETG',
      materialType: 'PETG',
      variants: [{ id: 'var-3', colorName: 'Jade White', colorHex: '#f5f5f0' }],
    },
  ],
};

function TestLayout() {
  return (
    <div>
      <Link to="/filament">Nav away</Link>
      <Outlet />
    </div>
  );
}

function FilamentPage() {
  return (
    <div>
      Filament page {useParams().id}
      {useLocation().search}
    </div>
  );
}

function renderRoute(initialPath = '/filament/spools/new') {
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

  const router = createMemoryRouter(
    [
      {
        element: <TestLayout />,
        children: [
          { path: '/filament/spools/new', element: <SpoolFormPage /> },
          { path: '/filament/:id', element: <FilamentPage /> },
          { path: '/filament', element: <div>Filament list page</div> },
        ],
      },
    ],
    { initialEntries: [initialPath] },
  );

  render(<RouterProvider router={router} />, { wrapper: Wrapper });
}

/** Answers the catalog, and the POST with the saved spools (one spool of filament f-1 unless told otherwise). */
function stubFetch(
  onPost?: (url: string, body: unknown) => void,
  {
    catalog = FILAMENTS_RESPONSE,
    saved = [{ id: 'spool-new', filamentId: 'f-1' }],
  }: { catalog?: unknown; saved?: unknown[] } = {},
) {
  vi.stubGlobal(
    'fetch',
    vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = input.toString();
      if (init?.method === 'POST') {
        onPost?.(url, JSON.parse(init.body as string));
        return Promise.resolve(jsonResponse({ spools: saved }, 201));
      }
      if (url.includes('/api/v1/filaments')) {
        return Promise.resolve(jsonResponse(catalog));
      }
      return Promise.resolve(jsonResponse({}));
    }),
  );
}

type User = ReturnType<typeof userEvent.setup>;

const line = (n: number) => screen.getByRole('group', { name: `Line ${n}` });

const filamentPicker = () =>
  screen.findByRole('combobox', { name: 'Filament' });

/** Chooses a color on a line's picker (the first line by default). */
async function chooseFilament(user: User, colorName = 'Black', lineNumber = 1) {
  await screen.findAllByRole('combobox', { name: 'Filament' });
  await user.click(
    within(line(lineNumber)).getByRole('combobox', { name: 'Filament' }),
  );
  await user.click(await screen.findByRole('option', { name: colorName }));
}

async function setField(
  user: User,
  label: string,
  value: string,
  scope: HTMLElement = document.body,
) {
  const field = within(scope).getByLabelText(label);
  await user.clear(field);
  if (value) await user.type(field, value);
}

const addButton = (name: string | RegExp = /^Add \d+ spools?$/) =>
  screen.getByRole('button', { name });

/** What the ring's arc covers of a full turn, 0 to 1; null when no color is chosen so there is no arc. */
function arcShare() {
  const dash = document
    .querySelector('[data-slot="spool-ring-fill"]')
    ?.getAttribute('stroke-dasharray');
  if (!dash) return null;
  const [filled, whole] = dash.split(' ').map(Number);
  return filled / whole;
}

describe('SpoolFormPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('opens on the filament picker', async () => {
    stubFetch();

    renderRoute();

    // Asked again each try: the popover swaps the trigger's node just after mount, and it is the new one that holds focus.
    await waitFor(() =>
      expect(screen.getByRole('combobox', { name: 'Filament' })).toHaveFocus(),
    );
  });

  it('shows a required-field error when submitted without a color', async () => {
    const user = userEvent.setup();
    stubFetch();

    renderRoute();

    await filamentPicker();
    await user.click(addButton('Add 1 spool'));

    expect(
      await screen.findByText('Choose a color', {
        selector: '[role=alert]',
      }),
    ).toBeInTheDocument();
  });

  it("adds one spool for the chosen color, shows a toast, and opens its filament's spools", async () => {
    const user = userEvent.setup();
    let postedBody: Record<string, unknown> | undefined;
    stubFetch((_url, body) => {
      postedBody = body as Record<string, unknown>;
    });

    renderRoute();

    await chooseFilament(user);
    await user.click(addButton('Add 1 spool'));

    await waitFor(() => expect(postedBody).toBeDefined());
    expect(postedBody).toMatchObject({
      lines: [
        {
          filamentVariantId: 'var-1',
          spoolWeightGrams: 1000,
          quantity: 1,
          remainingWeightGrams: null,
          price: null,
        },
      ],
      supplier: null,
      orderNumber: null,
      rfidId: null,
    });
    // The received date proposes today; the price lives on the lines only.
    expect(postedBody?.receivedOn).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    expect(postedBody).not.toHaveProperty('price');
    expect(await screen.findByText('Spool added.')).toBeInTheDocument();
    expect(
      await screen.findByText('Filament page f-1?tab=spools'),
    ).toBeInTheDocument();
  });

  it('adds a delivery of several lines in one save, with the purchase details they share', async () => {
    const user = userEvent.setup();
    let postedBody: unknown;
    let posts = 0;
    stubFetch(
      (_url, body) => {
        postedBody = body;
        posts += 1;
      },
      {
        saved: [
          { id: 's-1', filamentId: 'f-1' },
          { id: 's-2', filamentId: 'f-3' },
        ],
      },
    );
    renderRoute();

    await chooseFilament(user);
    await setField(user, 'Quantity', '180', line(1));
    await setField(user, 'Price per spool', '24', line(1));
    await user.click(screen.getByRole('button', { name: 'Add line' }));
    await chooseFilament(user, 'Jade White', 2);
    await setField(user, 'Quantity', '20', line(2));
    await setField(user, 'Supplier', ' Filament Shop ');
    await setField(user, 'Order number', 'PO-1042');
    fireEvent.change(screen.getByLabelText('Received on'), {
      target: { value: '2026-10-20' },
    });

    // The new line takes the delivery's spool weight and price.
    expect(within(line(2)).getByLabelText('Price per spool')).toHaveValue(24);
    expect(screen.getByText('200 spools · 200 kg · USD 4800.00')).toBeVisible();
    await user.click(addButton('Add 200 spools'));

    await waitFor(() => expect(postedBody).toBeDefined());
    expect(posts).toBe(1);
    expect(postedBody).toMatchObject({
      lines: [
        { filamentVariantId: 'var-1', quantity: 180, price: 24 },
        { filamentVariantId: 'var-3', quantity: 20, price: 24 },
      ],
      supplier: 'Filament Shop',
      orderNumber: 'PO-1042',
      receivedOn: '2026-10-20',
    });
    expect(await screen.findByText('200 spools added.')).toBeInTheDocument();
    // Two filaments: back to the list rather than one filament's page.
    expect(await screen.findByText('Filament list page')).toBeInTheDocument();
  });

  it('focuses the picker of a line it adds, and removes a line', async () => {
    const user = userEvent.setup();
    stubFetch();
    renderRoute();
    await filamentPicker();

    expect(
      screen.queryByRole('button', { name: /^Remove line/ }),
    ).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Add line' }));

    await waitFor(() =>
      expect(
        within(line(2)).getByRole('combobox', { name: 'Filament' }),
      ).toHaveFocus(),
    );
    await user.click(screen.getByRole('button', { name: 'Remove line 2' }));
    expect(
      screen.queryByRole('group', { name: 'Line 2' }),
    ).not.toBeInTheDocument();
  });

  it.each(['0', '501', '2.5'])(
    'refuses a quantity of %s and sends nothing',
    async (quantity) => {
      const user = userEvent.setup();
      const onPost = vi.fn();
      stubFetch(onPost);
      renderRoute();

      await chooseFilament(user);
      await setField(user, 'Quantity', quantity);
      await user.click(addButton('Add spools'));

      expect(
        await screen.findByText('Enter a whole number from 1 to 500'),
      ).toBeInTheDocument();
      expect(onPost).not.toHaveBeenCalled();
    },
  );

  it('refuses more than 1000 spools in one delivery', async () => {
    const user = userEvent.setup();
    const onPost = vi.fn();
    stubFetch(onPost);
    renderRoute();

    await chooseFilament(user);
    await setField(user, 'Quantity', '400', line(1));
    for (const n of [2, 3]) {
      await user.click(screen.getByRole('button', { name: 'Add line' }));
      await chooseFilament(user, 'Black', n);
      await setField(user, 'Quantity', '400', line(n));
    }
    await user.click(addButton('Add 1200 spools'));

    expect(
      await screen.findByText('Up to 1000 spools per delivery'),
    ).toBeInTheDocument();
    expect(onPost).not.toHaveBeenCalled();
  });

  it('treats a blank quantity as one spool, and keeps a price of 0 as 0', async () => {
    const user = userEvent.setup();
    let postedBody: unknown;
    stubFetch((_url, body) => {
      postedBody = body;
    });
    renderRoute();

    await chooseFilament(user);
    await user.clear(screen.getByLabelText('Quantity'));
    await setField(user, 'Price per spool', '0');
    await user.click(addButton('Add 1 spool'));

    await waitFor(() => expect(postedBody).toBeDefined());
    expect(postedBody).toMatchObject({ lines: [{ quantity: 1, price: 0 }] });
  });

  it('refuses a negative price and sends nothing', async () => {
    const user = userEvent.setup();
    const onPost = vi.fn();
    stubFetch(onPost);
    renderRoute();

    await chooseFilament(user);
    await setField(user, 'Price per spool', '-1');
    await user.click(addButton('Add 1 spool'));

    expect(await screen.findByText('Must be 0 or more')).toBeInTheDocument();
    expect(onPost).not.toHaveBeenCalled();
  });

  describe('left', () => {
    it('sends what is left of each spool, and nothing for a full one', async () => {
      const user = userEvent.setup();
      let postedBody: unknown;
      stubFetch((_url, body) => {
        postedBody = body;
      });
      renderRoute();
      await chooseFilament(user);

      expect(screen.getByLabelText('Left')).toHaveAttribute(
        'placeholder',
        'Full',
      );
      await setField(user, 'Left', '400');
      await user.click(addButton('Add 1 spool'));

      await waitFor(() => expect(postedBody).toBeDefined());
      expect(postedBody).toMatchObject({
        lines: [{ spoolWeightGrams: 1000, remainingWeightGrams: 400 }],
      });
    });

    it.each([
      ['0', 'Must be greater than 0'],
      ['1200', 'Exceeds spool weight'],
    ])('refuses %s g left and sends nothing', async (left, message) => {
      const user = userEvent.setup();
      const onPost = vi.fn();
      stubFetch(onPost);
      renderRoute();
      await chooseFilament(user);

      await setField(user, 'Left', left);
      await user.click(addButton('Add 1 spool'));

      expect(await screen.findByText(message)).toBeInTheDocument();
      expect(onPost).not.toHaveBeenCalled();
    });

    it('draws the chosen color on the ring, filled to what is left', async () => {
      const user = userEvent.setup();
      stubFetch();
      renderRoute();

      await filamentPicker();
      expect(arcShare()).toBeNull();
      await chooseFilament(user);
      expect(
        document.querySelector('[data-slot="spool-ring-fill"]'),
      ).toHaveAttribute('stroke', '#18181b');
      expect(arcShare()).toBeCloseTo(1);

      await setField(user, 'Left', '250');
      expect(arcShare()).toBeCloseTo(0.25);
    });
  });

  it('offers only the colors of the filament it was opened from, and goes back to its spools', async () => {
    const user = userEvent.setup();
    stubFetch();

    renderRoute('/filament/spools/new?filamentId=f-3');

    expect(
      await screen.findByRole('link', { name: 'Back to spools' }),
    ).toHaveAttribute('href', '/filament/f-3?tab=spools');
    await user.click(await filamentPicker());
    expect(
      await screen.findByRole('option', { name: 'Jade White' }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('option', { name: 'Black' }),
    ).not.toBeInTheDocument();
  });

  it('presets the color it is given', async () => {
    stubFetch();

    renderRoute('/filament/spools/new?filamentId=f-3&variantId=var-3');

    await waitFor(async () =>
      expect(await filamentPicker()).toHaveTextContent('Jade White'),
    );
  });

  it('blocks in-app navigation away while the form has unsaved changes', async () => {
    const user = userEvent.setup();
    stubFetch();

    renderRoute();

    await chooseFilament(user);

    await user.click(screen.getByRole('link', { name: 'Nav away' }));

    const dialog = await screen.findByRole('alertdialog');
    expect(
      within(dialog).getByText('Discard unsaved changes?'),
    ).toBeInTheDocument();
  });

  describe('filament picker', () => {
    const optionNames = () =>
      screen.queryAllByRole('option').map((option) => option.textContent);

    it('groups colors under their filament and lists every color of the catalog', async () => {
      const user = userEvent.setup();
      stubFetch();
      renderRoute();

      await user.click(await filamentPicker());

      expect(await screen.findByText('Bambu Lab · PLA Basic')).toBeVisible();
      expect(screen.getByText('Bambu Lab · PLA Matte')).toBeVisible();
      expect(screen.getByText('Polymaker · PETG')).toBeVisible();
      expect(optionNames()).toEqual(['Black', 'Charcoal', 'Jade White']);
    });

    it('matches every typed word, in any order, not scattered letters', async () => {
      const user = userEvent.setup();
      stubFetch();
      renderRoute();

      await user.click(await filamentPicker());
      const search = await screen.findByRole('combobox', {
        name: 'Search filament',
      });

      await user.type(search, 'matte');
      await waitFor(() => expect(optionNames()).toEqual(['Charcoal']));

      await user.clear(search);
      await user.type(search, 'matte bambu');
      await waitFor(() => expect(optionNames()).toEqual(['Charcoal']));

      await user.clear(search);
      await user.type(search, 'zzz');
      expect(await screen.findByText('No filament matches.')).toBeVisible();
    });

    it('shows the chosen color and its filament on the label', async () => {
      const user = userEvent.setup();
      stubFetch();
      renderRoute();

      expect(await filamentPicker()).toHaveTextContent('Choose a filament');

      await chooseFilament(user, 'Charcoal');

      expect(await filamentPicker()).toHaveTextContent('Charcoal');
      expect(await filamentPicker()).toHaveTextContent('Bambu Lab · PLA Matte');
      // The list closes on choosing.
      expect(screen.queryByRole('option')).not.toBeInTheDocument();
    });

    it('points to Add filament when the catalog is empty', async () => {
      const user = userEvent.setup();
      stubFetch(undefined, { catalog: { filaments: [] } });
      renderRoute();

      await user.click(await filamentPicker());

      expect(await screen.findByText('No filament yet.')).toBeVisible();
      expect(
        screen.getByRole('link', { name: 'Add filament' }),
      ).toHaveAttribute('href', '/filament/new');
    });
  });

  describe('quantity', () => {
    it('steps up and down with the buttons, stops at 1, and counts on the add button', async () => {
      const user = userEvent.setup();
      stubFetch();
      renderRoute();

      const quantity = await screen.findByLabelText('Quantity');
      expect(quantity).toHaveValue(1);
      expect(screen.getByRole('button', { name: 'Fewer' })).toHaveAttribute(
        'aria-disabled',
        'true',
      );

      await user.click(screen.getByRole('button', { name: 'More' }));
      await user.click(screen.getByRole('button', { name: 'More' }));
      expect(quantity).toHaveValue(3);
      expect(addButton('Add 3 spools')).toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: 'Fewer' }));
      await user.click(screen.getByRole('button', { name: 'Fewer' }));
      await user.click(screen.getByRole('button', { name: 'Fewer' }));
      expect(quantity).toHaveValue(1);
    });
  });

  describe('registering an RFID spool from Action required', () => {
    it('prefills what the printer read on one locked line, and adds one tagged spool', async () => {
      const user = userEvent.setup();
      let postedBody: unknown;
      stubFetch((_url, body) => {
        postedBody = body;
      });

      renderRoute(
        '/filament/spools/new?rfidId=TAG-1&slot=P01%20%C2%B7%20AMS%201%20%C2%B7%20Slot%202&variantId=var-1&spoolWeightGrams=1000&remainingWeightGrams=870',
      );

      expect(
        await screen.findByText(
          'Loads into P01 · AMS 1 · Slot 2 automatically.',
        ),
      ).toBeInTheDocument();
      expect(screen.queryByText('Quantity')).not.toBeInTheDocument();
      expect(
        screen.queryByRole('button', { name: 'Add line' }),
      ).not.toBeInTheDocument();
      expect(screen.getByLabelText('Left')).toHaveValue(870);

      await user.click(addButton('Add 1 spool'));

      await waitFor(() =>
        expect(postedBody).toMatchObject({
          lines: [
            {
              filamentVariantId: 'var-1',
              spoolWeightGrams: 1000,
              remainingWeightGrams: 870,
              quantity: 1,
            },
          ],
          rfidId: 'TAG-1',
        }),
      );
    });
  });
});
