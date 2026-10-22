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

const FILAMENTS_RESPONSE = {
  filaments: [
    {
      id: 'f-1',
      vendorName: 'Bambu Lab',
      material: 'PLA Basic',
      materialType: 'PLA',
      variants: [{ id: 'var-1', colorName: 'Black', colorHex: '#18181b' }],
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

function stubFetch(onPost?: (url: string, body: unknown) => void) {
  vi.stubGlobal(
    'fetch',
    vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = input.toString();
      if (init?.method === 'POST') {
        onPost?.(url, JSON.parse(init.body as string));
        return Promise.resolve(jsonResponse({ spools: [{ id: 'spool-new' }] }));
      }
      if (url.includes('/api/v1/filaments')) {
        return Promise.resolve(jsonResponse(FILAMENTS_RESPONSE));
      }
      return Promise.resolve(jsonResponse({}));
    }),
  );
}

describe('SpoolFormPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
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

    await user.click(await screen.findByRole('combobox', { name: 'Filament' }));
    await user.click(
      await screen.findByRole('option', {
        name: 'Bambu Lab · PLA Basic · Black',
      }),
    );

    await user.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => {
      expect(postedBody).toMatchObject({
        filamentVariantId: 'var-1',
        spoolWeightGrams: 1000,
      });
    });

    expect(await screen.findByText('Spools list page')).toBeInTheDocument();
  });

  it('blocks in-app navigation away while the form has unsaved changes', async () => {
    const user = userEvent.setup();
    stubFetch();

    renderRoute();

    await user.click(await screen.findByRole('combobox', { name: 'Filament' }));
    await user.click(
      await screen.findByRole('option', {
        name: 'Bambu Lab · PLA Basic · Black',
      }),
    );

    await user.click(screen.getByRole('link', { name: 'Nav away' }));

    const dialog = await screen.findByRole('alertdialog');
    expect(
      within(dialog).getByText('Discard unsaved changes?'),
    ).toBeInTheDocument();
  });

  describe('quantity and purchase info', () => {
    async function chooseBlack(user: ReturnType<typeof userEvent.setup>) {
      await user.click(
        await screen.findByRole('combobox', { name: 'Filament' }),
      );
      await user.click(
        await screen.findByRole('option', {
          name: 'Bambu Lab · PLA Basic · Black',
        }),
      );
    }

    async function setField(
      user: ReturnType<typeof userEvent.setup>,
      label: string,
      value: string,
    ) {
      const field = screen.getByLabelText(label);
      await user.clear(field);
      await user.type(field, value);
    }

    it('adds one spool with no purchase info by default', async () => {
      const user = userEvent.setup();
      let postedBody: Record<string, unknown> | undefined;
      stubFetch((_url, body) => {
        postedBody = body as Record<string, unknown>;
      });
      renderRoute();

      await chooseBlack(user);
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

      await chooseBlack(user);
      await setField(user, 'Quantity', '4');
      await setField(user, 'Price paid (per spool, optional)', '18.5');
      await setField(user, 'Supplier (optional)', ' Filament Shop ');
      await setField(user, 'Order number (optional)', 'PO-1042');
      fireEvent.change(screen.getByLabelText('Received on (optional)'), {
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

      await chooseBlack(user);
      await user.clear(screen.getByLabelText('Quantity'));
      await setField(user, 'Price paid (per spool, optional)', '0');
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

        await chooseBlack(user);
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

      await chooseBlack(user);
      await setField(user, 'Price paid (per spool, optional)', '-1');
      await user.click(screen.getByRole('button', { name: 'Save' }));

      expect(await screen.findByText('Must be 0 or more')).toBeInTheDocument();
      expect(onPost).not.toHaveBeenCalled();
    });

    it('shows the price per kg beside the price paid', async () => {
      const user = userEvent.setup();
      stubFetch();
      renderRoute();

      await setField(user, 'Price paid (per spool, optional)', '18.5');
      expect(await screen.findByText('18.50 per kg')).toBeInTheDocument();

      await setField(user, 'Spool weight (g)', '750');
      expect(await screen.findByText('24.67 per kg')).toBeInTheDocument();
    });
  });
});
