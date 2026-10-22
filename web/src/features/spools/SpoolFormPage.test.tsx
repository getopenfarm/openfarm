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
import { createMemoryRouter, Link, Outlet, RouterProvider } from 'react-router';
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
      <Link to="/spools">Nav away</Link>
      <Outlet />
    </div>
  );
}

function renderRoute(initialPath = '/spools/new') {
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
          { path: '/spools/new', element: <SpoolFormPage /> },
          { path: '/spools', element: <div>Spools list page</div> },
        ],
      },
    ],
    { initialEntries: [initialPath] },
  );

  render(<RouterProvider router={router} />, { wrapper: Wrapper });
}

function stubFetch(
  onPost?: (url: string, body: unknown) => void,
  catalog: unknown = FILAMENTS_RESPONSE,
) {
  vi.stubGlobal(
    'fetch',
    vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = input.toString();
      if (init?.method === 'POST') {
        onPost?.(url, JSON.parse(init.body as string));
        return Promise.resolve(jsonResponse({ spools: [{ id: 'spool-new' }] }));
      }
      if (url.includes('/api/v1/filaments')) {
        return Promise.resolve(jsonResponse(catalog));
      }
      return Promise.resolve(jsonResponse({}));
    }),
  );
}

type User = ReturnType<typeof userEvent.setup>;

const filamentPicker = () =>
  screen.findByRole('combobox', { name: 'Filament' });

async function chooseFilament(user: User, colorName = 'Black') {
  await user.click(await filamentPicker());
  await user.click(await screen.findByRole('option', { name: colorName }));
}

async function setField(user: User, label: string, value: string) {
  const field = screen.getByLabelText(label);
  await user.clear(field);
  if (value) await user.type(field, value);
}

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

    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(
      await screen.findByText('Choose a color', {
        selector: '[role=alert]',
      }),
    ).toBeInTheDocument();
  });

  it('registers a spool for the selected filament variant, shows a toast, and navigates to /spools', async () => {
    const user = userEvent.setup();
    let postedBody: unknown;
    stubFetch((_url, body) => {
      postedBody = body;
    });

    renderRoute();

    await chooseFilament(user);

    await user.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => {
      expect(postedBody).toMatchObject({
        filamentVariantId: 'var-1',
        spoolWeightGrams: 1000,
        remainingWeightGrams: null,
      });
    });

    expect(await screen.findByText('Spools list page')).toBeInTheDocument();
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
      stubFetch(undefined, { filaments: [] });
      renderRoute();

      await user.click(await filamentPicker());

      expect(await screen.findByText('No filament yet.')).toBeVisible();
      expect(
        screen.getByRole('link', { name: 'Add filament' }),
      ).toHaveAttribute('href', '/filament/new');
    });
  });

  describe('quantity', () => {
    it('steps up and down with the buttons, and stops at 1', async () => {
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

      await user.click(screen.getByRole('button', { name: 'Fewer' }));
      await user.click(screen.getByRole('button', { name: 'Fewer' }));
      await user.click(screen.getByRole('button', { name: 'Fewer' }));
      expect(quantity).toHaveValue(1);
    });
  });

  describe('partly used', () => {
    it('asks for the remaining weight only when switched on, and sends it with the spool', async () => {
      const user = userEvent.setup();
      let postedBody: unknown;
      stubFetch((_url, body) => {
        postedBody = body;
      });
      renderRoute();
      await chooseFilament(user);

      expect(
        screen.queryByLabelText('Remaining weight'),
      ).not.toBeInTheDocument();
      await user.click(screen.getByRole('switch', { name: 'Partly used' }));
      await setField(user, 'Remaining weight', '400');
      await user.click(screen.getByRole('button', { name: 'Save' }));

      await waitFor(() => expect(postedBody).toBeDefined());
      expect(postedBody).toMatchObject({
        spoolWeightGrams: 1000,
        remainingWeightGrams: 400,
      });
    });

    it('sends no remaining weight once switched off again', async () => {
      const user = userEvent.setup();
      let postedBody: unknown;
      stubFetch((_url, body) => {
        postedBody = body;
      });
      renderRoute();
      await chooseFilament(user);

      await user.click(screen.getByRole('switch', { name: 'Partly used' }));
      await setField(user, 'Remaining weight', '400');
      await user.click(screen.getByRole('switch', { name: 'Partly used' }));
      await user.click(screen.getByRole('button', { name: 'Save' }));

      await waitFor(() => expect(postedBody).toBeDefined());
      expect(postedBody).toMatchObject({ remainingWeightGrams: null });
    });

    it.each([
      ['', 'Enter the remaining weight'],
      ['0', 'Must be greater than 0'],
      ['1200', 'Exceeds spool weight'],
    ])(
      'refuses a remaining weight of "%s" and sends nothing',
      async (remaining, message) => {
        const user = userEvent.setup();
        const onPost = vi.fn();
        stubFetch(onPost);
        renderRoute();
        await chooseFilament(user);

        await user.click(screen.getByRole('switch', { name: 'Partly used' }));
        await setField(user, 'Remaining weight', remaining);
        await user.click(screen.getByRole('button', { name: 'Save' }));

        expect(await screen.findByText(message)).toBeInTheDocument();
        expect(onPost).not.toHaveBeenCalled();
      },
    );
  });

  describe('label recap', () => {
    it('stays empty until a color is chosen', async () => {
      stubFetch();
      renderRoute();

      await filamentPicker();

      expect(screen.queryByText('1 spool · 1 kg')).not.toBeInTheDocument();
      expect(arcShare()).toBeNull();
    });

    it('recaps the spools, their weight and their price', async () => {
      const user = userEvent.setup();
      stubFetch();
      renderRoute();

      await chooseFilament(user);
      expect(screen.getByText('1 spool · 1 kg')).toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: 'More' }));
      await user.click(screen.getByRole('button', { name: 'More' }));
      await setField(user, 'Price per spool', '18.5');

      expect(
        screen.getByText('3 spools · 1 kg each · 3 kg total'),
      ).toBeInTheDocument();
      expect(
        screen.getByText('18.50 per spool · 55.50 total'),
      ).toBeInTheDocument();

      await user.click(screen.getByRole('switch', { name: 'Partly used' }));
      await setField(user, 'Remaining weight', '400');

      expect(
        screen.getByText('3 spools · 400 g left of 1 kg each'),
      ).toBeInTheDocument();
    });

    it('draws the chosen color on the ring, filled to what is left', async () => {
      const user = userEvent.setup();
      stubFetch();
      renderRoute();

      await chooseFilament(user);
      expect(
        document.querySelector('[data-slot="spool-ring-fill"]'),
      ).toHaveAttribute('stroke', '#18181b');
      expect(arcShare()).toBeCloseTo(1);

      await user.click(screen.getByRole('switch', { name: 'Partly used' }));
      await setField(user, 'Remaining weight', '250');
      expect(arcShare()).toBeCloseTo(0.25);
    });
  });

  describe('quantity and purchase info', () => {
    it('adds one spool with no purchase info by default', async () => {
      const user = userEvent.setup();
      let postedBody: Record<string, unknown> | undefined;
      stubFetch((_url, body) => {
        postedBody = body as Record<string, unknown>;
      });
      renderRoute();

      await chooseFilament(user);
      await user.click(screen.getByRole('button', { name: 'Save' }));

      await waitFor(() => expect(postedBody).toBeDefined());
      expect(postedBody).toMatchObject({
        quantity: 1,
        price: null,
        supplier: null,
        orderNumber: null,
      });
      // The received date proposes today.
      expect(postedBody?.receivedOn).toMatch(/^\d{4}-\d{2}-\d{2}$/);
      expect(await screen.findByText('Spool added.')).toBeInTheDocument();
    });

    it('adds several identical spools with their purchase info in one save', async () => {
      const user = userEvent.setup();
      let postedBody: unknown;
      let posts = 0;
      stubFetch((_url, body) => {
        postedBody = body;
        posts += 1;
      });
      renderRoute();

      await chooseFilament(user);
      await setField(user, 'Quantity', '4');
      await setField(user, 'Price per spool', '18.5');
      await setField(user, 'Supplier', ' Filament Shop ');
      await setField(user, 'Order number', 'PO-1042');
      fireEvent.change(screen.getByLabelText('Received on'), {
        target: { value: '2026-10-20' },
      });
      await user.click(screen.getByRole('button', { name: 'Save' }));

      await waitFor(() => expect(postedBody).toBeDefined());
      expect(posts).toBe(1);
      expect(postedBody).toMatchObject({
        filamentVariantId: 'var-1',
        quantity: 4,
        price: 18.5,
        supplier: 'Filament Shop',
        orderNumber: 'PO-1042',
        receivedOn: '2026-10-20',
      });
      expect(await screen.findByText('4 spools added.')).toBeInTheDocument();
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
      await user.click(screen.getByRole('button', { name: 'Save' }));

      await waitFor(() => expect(postedBody).toBeDefined());
      expect(postedBody).toMatchObject({ quantity: 1, price: 0 });
    });

    it.each(['0', '51', '2.5'])(
      'refuses a quantity of %s and sends nothing',
      async (quantity) => {
        const user = userEvent.setup();
        const onPost = vi.fn();
        stubFetch(onPost);
        renderRoute();

        await chooseFilament(user);
        await setField(user, 'Quantity', quantity);
        await user.click(screen.getByRole('button', { name: 'Save' }));

        expect(
          await screen.findByText('Enter a whole number from 1 to 50'),
        ).toBeInTheDocument();
        expect(onPost).not.toHaveBeenCalled();
      },
    );

    it('refuses a negative price and sends nothing', async () => {
      const user = userEvent.setup();
      const onPost = vi.fn();
      stubFetch(onPost);
      renderRoute();

      await chooseFilament(user);
      await setField(user, 'Price per spool', '-1');
      await user.click(screen.getByRole('button', { name: 'Save' }));

      expect(await screen.findByText('Must be 0 or more')).toBeInTheDocument();
      expect(onPost).not.toHaveBeenCalled();
    });

    it('shows the price per kg beside the price paid', async () => {
      const user = userEvent.setup();
      stubFetch();
      renderRoute();

      await setField(user, 'Price per spool', '18.5');
      expect(await screen.findByText('18.50 per kg')).toBeInTheDocument();

      await setField(user, 'Spool weight', '750');
      expect(await screen.findByText('24.67 per kg')).toBeInTheDocument();
    });
  });

  describe('save and add another', () => {
    it('saves, stays on the page, and keeps what a delivery shares for the next color', async () => {
      const user = userEvent.setup();
      let postedBody: unknown;
      let posts = 0;
      stubFetch((_url, body) => {
        postedBody = body;
        posts += 1;
      });
      renderRoute();

      await chooseFilament(user);
      await user.click(screen.getByRole('button', { name: 'More' }));
      await setField(user, 'Spool weight', '750');
      await setField(user, 'Label', 'Shelf B');
      await setField(user, 'Price per spool', '18.5');
      await setField(user, 'Supplier', 'Filament Shop');
      await setField(user, 'Order number', 'PO-1042');
      fireEvent.change(screen.getByLabelText('Received on'), {
        target: { value: '2026-10-20' },
      });
      await user.click(
        screen.getByRole('button', { name: 'Save and add another' }),
      );

      expect(await screen.findByText('2 spools added.')).toBeInTheDocument();
      expect(posts).toBe(1);
      expect(postedBody).toMatchObject({
        filamentVariantId: 'var-1',
        quantity: 2,
        spoolWeightGrams: 750,
        label: 'Shelf B',
        price: 18.5,
      });
      expect(screen.queryByText('Spools list page')).not.toBeInTheDocument();

      // What the delivery shares stays; what differs per color starts again.
      expect(screen.getByLabelText('Spool weight')).toHaveValue(750);
      expect(screen.getByLabelText('Supplier')).toHaveValue('Filament Shop');
      expect(screen.getByLabelText('Order number')).toHaveValue('PO-1042');
      expect(screen.getByLabelText('Received on')).toHaveValue('2026-10-20');
      const picker = await filamentPicker();
      expect(picker).toHaveTextContent('Choose a filament');
      expect(screen.getByLabelText('Quantity')).toHaveValue(1);
      expect(screen.getByLabelText('Label')).toHaveValue('');
      expect(screen.getByLabelText('Price per spool')).toHaveValue(null);
      await waitFor(() => expect(picker).toHaveFocus());
    });

    it('asks for a color first, and sends nothing', async () => {
      const user = userEvent.setup();
      const onPost = vi.fn();
      stubFetch(onPost);
      renderRoute();

      await user.click(
        await screen.findByRole('button', { name: 'Save and add another' }),
      );

      expect(
        await screen.findByText('Choose a color', { selector: '[role=alert]' }),
      ).toBeInTheDocument();
      expect(onPost).not.toHaveBeenCalled();
    });
  });
  describe('registering an RFID spool from Action required', () => {
    it('prefills what the printer read, adds one tagged spool, and offers no quantity', async () => {
      const user = userEvent.setup();
      let postedBody: unknown;
      stubFetch((_url, body) => {
        postedBody = body;
      });

      renderRoute(
        '/spools/new?rfidId=TAG-1&slot=P01%20%C2%B7%20AMS%201%20%C2%B7%20Slot%202&variantId=var-1&spoolWeightGrams=1000&remainingWeightGrams=870',
      );

      expect(
        await screen.findByText(
          'Loads into P01 · AMS 1 · Slot 2 automatically.',
        ),
      ).toBeInTheDocument();
      expect(screen.queryByText('Quantity')).not.toBeInTheDocument();
      expect(
        screen.queryByRole('button', { name: 'Save and add another' }),
      ).not.toBeInTheDocument();
      expect(screen.getByLabelText('Remaining weight')).toHaveValue(870);

      await user.click(screen.getByRole('button', { name: 'Save' }));

      await waitFor(() =>
        expect(postedBody).toMatchObject({
          filamentVariantId: 'var-1',
          spoolWeightGrams: 1000,
          remainingWeightGrams: 870,
          quantity: 1,
          rfidId: 'TAG-1',
        }),
      );
    });
  });
});
