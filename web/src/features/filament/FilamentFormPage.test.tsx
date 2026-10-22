import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { createMemoryRouter, Link, Outlet, RouterProvider } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Toaster } from '@/components/ui/sonner';
import type {
  FilamentResponse,
  VariantStockResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { FilamentFormPage } from './FilamentFormPage';

const FILAMENTS: FilamentResponse[] = [
  {
    id: 'f-1',
    vendorId: 'v-1',
    vendorName: 'Prusament',
    material: 'PLA',
    materialType: 'PLA',
    variants: [{ id: 'var-1', colorName: 'Galaxy Black', colorHex: '#18181b' }],
  },
];

const STOCK: VariantStockResponse[] = [
  {
    filamentVariantId: 'var-1',
    onHandGrams: 1200,
    activeSpoolCount: 3,
    stockValue: 30,
    unpricedSpoolCount: 0,
    usedLast30DaysGrams: 410,
    thresholdGrams: 1500,
    isLow: true,
  },
];

const SAVED_ASSIGNMENT = {
  brand: 'Bambu',
  modelCode: 'X1C',
  nozzleDiameterMm: 0.6,
  filamentProfileId: 'fp-old',
  colorHex: null,
};

const VENDORS = [{ id: 'v-1', name: 'Prusament' }];

function stubFetch(onRequest?: (url: string, init?: RequestInit) => void) {
  vi.stubGlobal(
    'fetch',
    vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = input.toString();
      const method = init?.method?.toUpperCase() ?? 'GET';
      onRequest?.(url, init);

      if (method === 'GET' && url.endsWith('/api/v1/filaments')) {
        return Promise.resolve(
          new Response(JSON.stringify({ filaments: FILAMENTS, stock: STOCK }), {
            status: 200,
            headers: { 'Content-Type': 'application/json' },
          }),
        );
      }
      if (method === 'GET' && url.includes('/api/v1/vendors')) {
        return Promise.resolve(
          new Response(JSON.stringify({ vendors: VENDORS }), {
            status: 200,
            headers: { 'Content-Type': 'application/json' },
          }),
        );
      }
      if (
        method === 'GET' &&
        url.includes('/api/v1/filaments/profile-assignment-fleet')
      ) {
        return Promise.resolve(
          new Response(
            JSON.stringify({
              variants: [
                {
                  brand: 'Bambu',
                  modelCode: 'P1S',
                  nozzleDiameterMm: 0.4,
                  printerCount: 2,
                },
              ],
              printersAwaitingNozzle: [],
            }),
            { status: 200, headers: { 'Content-Type': 'application/json' } },
          ),
        );
      }
      if (method === 'GET' && url.includes('/api/v1/profiles/filament')) {
        // System-origin profiles only surface via the picker-style query (origin/materialType
        // params) — a call with none of those params hits the legacy User-only browse path and
        // must NOT be what feeds this picker (that was the bug: the dropdown looked empty because
        // it went down that path and only System rows existed for most material types).
        if (!url.includes('origin=') && !url.includes('materialType=')) {
          return Promise.resolve(
            new Response(JSON.stringify({ items: [] }), {
              status: 200,
              headers: { 'Content-Type': 'application/json' },
            }),
          );
        }
        return Promise.resolve(
          new Response(
            JSON.stringify({
              items: [
                {
                  id: 'fp-1',
                  name: 'Generic PETG',
                  origin: 'System',
                  vendorName: 'Generic',
                  materialType: 'PETG',
                },
              ],
            }),
            { status: 200, headers: { 'Content-Type': 'application/json' } },
          ),
        );
      }
      if (method === 'GET' && url.endsWith('/f-1/profile-assignments')) {
        return Promise.resolve(
          new Response(
            JSON.stringify({ assignments: [SAVED_ASSIGNMENT], issues: [] }),
            { status: 200, headers: { 'Content-Type': 'application/json' } },
          ),
        );
      }
      if (method === 'PUT' && url.endsWith('/api/v1/filaments/f-1')) {
        const body = JSON.parse(init?.body as string);
        return Promise.resolve(
          new Response(
            JSON.stringify({
              ...FILAMENTS[0],
              variants: [
                ...(FILAMENTS[0].variants ?? []),
                ...body.newVariants.map(
                  (v: { colorName: string; colorHex: string }, i: number) => ({
                    id: `var-new-${i}`,
                    ...v,
                  }),
                ),
              ],
              profileAssignments: body.profileAssignments,
            }),
            { status: 200, headers: { 'Content-Type': 'application/json' } },
          ),
        );
      }
      if (method === 'POST' && url.endsWith('/api/v1/vendors')) {
        return Promise.resolve(
          new Response(JSON.stringify({ id: 'v-new', name: 'Sunlu' }), {
            status: 201,
            headers: { 'Content-Type': 'application/json' },
          }),
        );
      }
      if (method === 'POST' && url.endsWith('/api/v1/filaments')) {
        return Promise.resolve(
          new Response(JSON.stringify({ id: 'f-new' }), {
            status: 200,
            headers: { 'Content-Type': 'application/json' },
          }),
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

function TestLayout() {
  return (
    <div>
      <Link to="/filament">Nav away</Link>
      <Outlet />
    </div>
  );
}

function renderAddPage(onRequest?: (url: string, init?: RequestInit) => void) {
  stubFetch(onRequest);
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
          {
            path: '/filament/new',
            element: <FilamentFormPage mode="add" />,
          },
          { path: '/filament', element: <div>Filament catalog page</div> },
        ],
      },
    ],
    { initialEntries: ['/filament/new'] },
  );
  render(<RouterProvider router={router} />, { wrapper: Wrapper });
}

function renderEditPage(
  id: string,
  onRequest?: (url: string, init?: RequestInit) => void,
) {
  stubFetch(onRequest);
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
          {
            path: '/filament/:id/edit',
            element: <FilamentFormPage mode="edit" />,
          },
          { path: '/filament', element: <div>Filament catalog page</div> },
        ],
      },
    ],
    { initialEntries: [`/filament/${id}/edit`] },
  );
  render(<RouterProvider router={router} />, { wrapper: Wrapper });
}

describe('FilamentFormPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('shows inline field errors when submitted with no vendor, material, or variant', async () => {
    const user = userEvent.setup();
    renderAddPage();

    await screen.findByRole('combobox', { name: 'Vendor' });
    await user.click(screen.getByRole('button', { name: 'Add filament' }));

    expect(await screen.findByText('Vendor is required')).toBeInTheDocument();
    expect(screen.getByText('Name is required')).toBeInTheDocument();
    expect(screen.getByText('Add at least one variant')).toBeInTheDocument();
  });

  it('adds a new filament with a variant, shows a toast, and navigates to /filament', async () => {
    const user = userEvent.setup();
    let createBody: unknown;
    renderAddPage((url, init) => {
      if (init?.method === 'POST' && url.endsWith('/api/v1/filaments')) {
        createBody = JSON.parse(init.body as string);
      }
    });

    await user.click(await screen.findByRole('combobox', { name: 'Vendor' }));
    await user.click(await screen.findByRole('option', { name: 'Prusament' }));

    await user.type(screen.getByLabelText('Name'), 'PETG');

    await user.click(screen.getByRole('button', { name: /add variant/i }));
    await user.clear(within(screen.getByRole('dialog')).getByLabelText('Name'));
    await user.type(
      within(screen.getByRole('dialog')).getByLabelText('Name'),
      'Galaxy Black',
    );
    await user.click(screen.getByRole('button', { name: 'Black' }));
    await user.click(screen.getByRole('button', { name: 'Add variant' }));

    await user.click(screen.getByRole('button', { name: 'Add filament' }));

    await waitFor(() => {
      expect(createBody).toMatchObject({
        vendorId: 'v-1',
        material: 'PETG',
        variants: [{ colorName: 'Galaxy Black' }],
      });
    });
    expect(
      await screen.findByText('Filament catalog page'),
    ).toBeInTheDocument();
  });

  it('creates a typed new vendor on save, then the filament with its id', async () => {
    const user = userEvent.setup();
    const writes: { url: string; body: unknown }[] = [];
    renderAddPage((url, init) => {
      if (init?.method === 'POST') {
        writes.push({ url, body: JSON.parse(init.body as string) });
      }
    });

    await user.type(
      await screen.findByRole('combobox', { name: 'Vendor' }),
      'Sunlu',
    );
    await user.click(
      await screen.findByRole('option', { name: /Add "Sunlu"/ }),
    );
    expect(screen.getByText('New')).toBeInTheDocument();
    expect(writes).toEqual([]);

    await user.type(screen.getByLabelText('Name'), 'PLA+');
    await user.click(screen.getByRole('button', { name: /add variant/i }));
    await user.clear(within(screen.getByRole('dialog')).getByLabelText('Name'));
    await user.type(
      within(screen.getByRole('dialog')).getByLabelText('Name'),
      'Black',
    );
    await user.click(screen.getByRole('button', { name: 'Add variant' }));
    await user.click(screen.getByRole('button', { name: 'Add filament' }));

    await waitFor(() => expect(writes).toHaveLength(2));
    expect(writes[0]).toMatchObject({
      url: expect.stringMatching(/\/api\/v1\/vendors$/),
      body: { name: 'Sunlu' },
    });
    expect(writes[1]).toMatchObject({
      url: expect.stringMatching(/\/api\/v1\/filaments$/),
      body: { vendorId: 'v-new', material: 'PLA+' },
    });
  });

  it('lists System-origin profiles matching the selected material type and links one', async () => {
    const user = userEvent.setup();
    let createBody: unknown;
    renderAddPage((url, init) => {
      if (init?.method === 'POST' && url.endsWith('/api/v1/filaments')) {
        createBody = JSON.parse(init.body as string);
      }
    });

    await user.click(await screen.findByRole('combobox', { name: 'Vendor' }));
    await user.click(await screen.findByRole('option', { name: 'Prusament' }));
    await user.type(screen.getByLabelText('Name'), 'PETG Basic');

    await user.click(
      screen.getByRole('button', { name: 'Default profile for P1S · 0.4 mm' }),
    );
    expect(
      await screen.findByRole('option', { name: /Generic PETG/ }),
    ).toBeInTheDocument();
    await user.click(screen.getByRole('option', { name: /Generic PETG/ }));

    await user.click(screen.getByRole('button', { name: /add variant/i }));
    await user.clear(within(screen.getByRole('dialog')).getByLabelText('Name'));
    await user.type(
      within(screen.getByRole('dialog')).getByLabelText('Name'),
      'Galaxy Black',
    );
    await user.click(screen.getByRole('button', { name: 'Black' }));
    await user.click(screen.getByRole('button', { name: 'Add variant' }));

    await user.click(screen.getByRole('button', { name: 'Add filament' }));

    await waitFor(() => {
      expect(createBody).toMatchObject({
        profileAssignments: [
          {
            brand: 'Bambu',
            modelCode: 'P1S',
            nozzleDiameterMm: 0.4,
            filamentProfileId: 'fp-1',
          },
        ],
      });
    });
  });

  it('suggests the type from the name until the user picks one', async () => {
    const user = userEvent.setup();
    renderAddPage();

    const name = await screen.findByLabelText('Name');
    const type = screen.getByRole('combobox', { name: 'Type' });
    await user.type(name, 'PETG Basic');
    expect(type).toHaveTextContent('PETG');
    expect(screen.getByText('Detected from name')).toBeInTheDocument();

    await user.click(type);
    await user.click(await screen.findByRole('option', { name: 'ABS' }));
    expect(screen.queryByText('Detected from name')).not.toBeInTheDocument();
    await user.clear(name);
    await user.type(name, 'PLA Matte');
    expect(type).toHaveTextContent('ABS');
  });

  it('does not change the type from the name in edit mode', async () => {
    const user = userEvent.setup();
    renderEditPage('f-1');

    const name = await screen.findByLabelText('Name');
    await user.clear(name);
    await user.type(name, 'PETG Basic');
    expect(screen.getByRole('combobox', { name: 'Type' })).toHaveTextContent(
      'PLA',
    );
  });

  it('blocks in-app navigation away while the form has unsaved changes', async () => {
    const user = userEvent.setup();
    renderAddPage();

    await user.type(await screen.findByLabelText('Name'), 'PETG');

    await user.click(screen.getByRole('link', { name: 'Nav away' }));

    const dialog = await screen.findByRole('alertdialog');
    expect(
      within(dialog).getByText('Discard unsaved changes?'),
    ).toBeInTheDocument();
  });

  it('shows existing filament details and variants in edit mode', async () => {
    renderEditPage('f-1');

    expect(await screen.findByText(/Prusament · PLA/)).toBeInTheDocument();
    expect(screen.getByText('Variants (1)')).toBeInTheDocument();
    expect(screen.getByText('Galaxy Black')).toBeInTheDocument();
  });

  it('stages a new variant and assignment change in the draft and saves them in one PUT', async () => {
    const user = userEvent.setup();
    const writes: { url: string; method: string; body: unknown }[] = [];
    renderEditPage('f-1', (url, init) => {
      const method = init?.method?.toUpperCase() ?? 'GET';
      if (method !== 'GET') {
        writes.push({ url, method, body: JSON.parse(init?.body as string) });
      }
    });

    await screen.findByText('Variants (1)');
    const save = screen.getByRole('button', { name: 'Save' });
    expect(save).toBeDisabled();
    // Saved variants cannot be removed.
    expect(
      screen.queryByRole('button', { name: 'Remove Galaxy Black' }),
    ).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /add variant/i }));
    const dialog = within(screen.getByRole('dialog'));
    await user.clear(dialog.getByLabelText('Name'));
    await user.type(dialog.getByLabelText('Name'), 'Red');
    await user.click(dialog.getByRole('button', { name: 'Red' }));
    await user.click(dialog.getByRole('button', { name: 'Add variant' }));

    expect(await screen.findByText('Variants (2)')).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: 'Remove Red' }),
    ).toBeInTheDocument();

    await user.click(
      screen.getByRole('button', { name: 'Default profile for P1S · 0.4 mm' }),
    );
    await user.click(
      await screen.findByRole('option', { name: /Generic PETG/ }),
    );

    expect(writes).toEqual([]);

    await user.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(writes).toHaveLength(1));
    expect(writes[0]).toMatchObject({
      method: 'PUT',
      url: expect.stringMatching(/\/api\/v1\/filaments\/f-1$/),
      body: {
        vendorId: 'v-1',
        material: 'PLA',
        materialType: 'PLA',
        newVariants: [{ colorName: 'Red', colorHex: '#ef4444' }],
        profileAssignments: [
          SAVED_ASSIGNMENT,
          {
            brand: 'Bambu',
            modelCode: 'P1S',
            nozzleDiameterMm: 0.4,
            filamentProfileId: 'fp-1',
            colorHex: null,
          },
        ],
      },
    });
    expect(await screen.findByText('Filament saved.')).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled(),
    );
    expect(
      screen.queryByRole('button', { name: 'Remove Red' }),
    ).not.toBeInTheDocument();
  });

  it('edits name and type in place; a type change clears profile assignments', async () => {
    const user = userEvent.setup();
    const writes: { method: string; body: unknown }[] = [];
    renderEditPage('f-1', (_url, init) => {
      const method = init?.method?.toUpperCase() ?? 'GET';
      if (method !== 'GET') {
        writes.push({ method, body: JSON.parse(init?.body as string) });
      }
    });

    const name = await screen.findByLabelText('Name');
    await user.clear(name);
    await user.type(name, 'PLA Matte');
    await user.click(screen.getByRole('combobox', { name: 'Type' }));
    await user.click(await screen.findByRole('option', { name: 'PETG' }));
    await user.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(writes).toHaveLength(1));
    expect(writes[0]).toMatchObject({
      method: 'PUT',
      body: {
        vendorId: 'v-1',
        material: 'PLA Matte',
        materialType: 'PETG',
        profileAssignments: [],
      },
    });
  });
  it('saves default and variant prices together, then clears an override to inherit', async () => {
    const user = userEvent.setup();
    const writes: Record<string, unknown>[] = [];
    renderEditPage('f-1', (_url, init) => {
      if (init?.method === 'PUT') writes.push(JSON.parse(init.body as string));
    });
    const price = await screen.findByRole('spinbutton', {
      name: 'Price per kg',
    });
    // Variant prices are not inline form fields; they live in the variant dialog.
    expect(
      screen
        .getAllByRole('spinbutton')
        .map((input) => input.getAttribute('name')),
    ).toEqual(['pricePerKg', 'lowStockGrams']);
    expect(screen.getByText('No price')).toBeInTheDocument();
    await user.type(price, '25.5');
    expect(screen.getByText('25.50/kg default')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Edit Galaxy Black' }));
    let dialog = within(screen.getByRole('dialog'));
    const override = dialog.getByLabelText('Price per kg');
    expect(override).toHaveAttribute('placeholder', 'Default: 25.50');
    await user.type(override, '30');
    await user.click(dialog.getByRole('button', { name: 'Update variant' }));
    expect(screen.getByText('30.00/kg')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(writes).toHaveLength(1));
    expect(writes[0]).toMatchObject({
      pricePerKg: 25.5,
      variantPrices: [{ variantId: 'var-1', pricePerKg: 30 }],
    });
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled(),
    );

    await user.click(screen.getByRole('button', { name: 'Edit Galaxy Black' }));
    dialog = within(screen.getByRole('dialog'));
    await user.clear(dialog.getByLabelText('Price per kg'));
    await user.click(dialog.getByRole('button', { name: 'Update variant' }));
    await user.clear(price);
    await user.type(price, '25');
    await user.click(screen.getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(writes).toHaveLength(2));
    expect(writes[1]).toMatchObject({
      pricePerKg: 25,
      variantPrices: [{ variantId: 'var-1', pricePerKg: null }],
    });
  });

  it('stages a color profile override from the variant dialog', async () => {
    const user = userEvent.setup();
    const writes: Record<string, unknown>[] = [];
    renderEditPage('f-1', (_url, init) => {
      if (init?.method === 'PUT') writes.push(JSON.parse(init.body as string));
    });

    await user.click(
      await screen.findByRole('button', { name: 'Edit Galaxy Black' }),
    );
    const dialog = within(screen.getByRole('dialog'));
    await user.click(
      dialog.getByRole('button', { name: 'P1S · 0.4 mm profile' }),
    );
    await user.click(
      await screen.findByRole('option', { name: /Generic PETG/ }),
    );
    await user.click(dialog.getByRole('button', { name: 'Update variant' }));
    expect(screen.getByText('1 profile override')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(writes).toHaveLength(1));
    expect(writes[0]).toMatchObject({
      profileAssignments: [
        SAVED_ASSIGNMENT,
        {
          brand: 'Bambu',
          modelCode: 'P1S',
          nozzleDiameterMm: 0.4,
          filamentProfileId: 'fp-1',
          colorHex: '#18181b',
        },
      ],
    });
  });

  it('rejects negative prices before sending a request', async () => {
    const user = userEvent.setup();
    const writes: unknown[] = [];
    renderEditPage('f-1', (_url, init) => {
      if (init?.method === 'PUT') writes.push(init.body);
    });
    const price = await screen.findByRole('spinbutton', {
      name: 'Price per kg',
    });
    await user.type(price, '-1');
    await user.click(screen.getByRole('button', { name: 'Save' }));
    expect(writes).toEqual([]);
    expect(
      await screen.findByText('Price must be non-negative'),
    ).toBeInTheDocument();
    expect(price).toBeInvalid();
  });
  it("shows each saved color's stock and saves low-stock thresholds with the prices", async () => {
    const user = userEvent.setup();
    const writes: Record<string, unknown>[] = [];
    renderEditPage('f-1', (_url, init) => {
      if (init?.method === 'PUT') writes.push(JSON.parse(init.body as string));
    });

    expect(
      await screen.findByText('1.2 kg in 3 spools · 410 g used in 30 d'),
    ).toBeInTheDocument();
    expect(screen.getByText('Low')).toBeInTheDocument();

    await user.type(
      screen.getByRole('spinbutton', { name: 'Low stock below' }),
      '1500',
    );
    await user.click(screen.getByRole('button', { name: 'Edit Galaxy Black' }));
    const dialog = within(screen.getByRole('dialog'));
    expect(dialog.getByLabelText('Low stock below')).toHaveAttribute(
      'placeholder',
      'Default: 1.5 kg',
    );
    await user.type(dialog.getByLabelText('Low stock below'), '0');
    await user.click(dialog.getByRole('button', { name: 'Update variant' }));
    await user.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(writes).toHaveLength(1));
    expect(writes[0]).toMatchObject({
      lowStockGrams: 1500,
      variantPrices: [{ variantId: 'var-1', lowStockGrams: 0 }],
    });
  });
});
