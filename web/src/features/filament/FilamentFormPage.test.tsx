import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { FilamentResponse } from '@/lib/api/generated/endpoints.schemas';
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

const VENDORS = [{ id: 'v-1', name: 'Prusament' }];

function stubFetch(onRequest?: (url: string, init?: RequestInit) => void) {
  vi.stubGlobal(
    'fetch',
    vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = input.toString();
      const method = init?.method?.toUpperCase() ?? 'GET';
      onRequest?.(url, init);

      if (method === 'GET' && url.includes('/api/v1/filaments')) {
        return Promise.resolve(
          new Response(JSON.stringify({ filaments: FILAMENTS }), {
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
      if (method === 'GET' && url.includes('/api/v1/profiles/filament')) {
        return Promise.resolve(
          new Response(JSON.stringify({ profiles: [] }), {
            status: 200,
            headers: { 'Content-Type': 'application/json' },
          }),
        );
      }
      if (method === 'POST' && url.includes('/variants')) {
        return Promise.resolve(
          new Response(
            JSON.stringify({
              id: 'var-new',
              colorName: 'Red',
              colorHex: '#ef4444',
            }),
            { status: 200, headers: { 'Content-Type': 'application/json' } },
          ),
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

function renderAddPage(onRequest?: (url: string, init?: RequestInit) => void) {
  stubFetch(onRequest);
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/filament/new']}>
        <Routes>
          <Route
            path="/filament/new"
            element={<FilamentFormPage mode="add" />}
          />
          <Route path="/filament" element={<div>Filament catalog page</div>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

function renderEditPage(
  id: string,
  onRequest?: (url: string, init?: RequestInit) => void,
) {
  stubFetch(onRequest);
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[`/filament/${id}/edit`]}>
        <Routes>
          <Route
            path="/filament/:id/edit"
            element={<FilamentFormPage mode="edit" />}
          />
          <Route path="/filament" element={<div>Filament catalog page</div>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('FilamentFormPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('adds a new filament with a variant', async () => {
    const user = userEvent.setup();
    let createBody: unknown;
    renderAddPage((url, init) => {
      if (init?.method === 'POST' && url.endsWith('/api/v1/filaments')) {
        createBody = JSON.parse(init.body as string);
      }
    });

    await user.click(await screen.findByRole('combobox', { name: 'Vendor' }));
    await user.click(await screen.findByRole('option', { name: 'Prusament' }));

    await user.type(screen.getByLabelText('Material name'), 'PETG');

    await user.click(screen.getByRole('button', { name: /add variant/i }));
    await user.type(screen.getByLabelText('Name'), 'Galaxy Black');
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

  it('shows existing filament details and variants in edit mode', async () => {
    renderEditPage('f-1');

    expect(
      await screen.findByText(/Prusament · PLA · PLA/),
    ).toBeInTheDocument();
    expect(screen.getByText('Variants (1)')).toBeInTheDocument();
    expect(screen.getByText('Galaxy Black')).toBeInTheDocument();
  });

  it('adds a variant to an existing filament via the API', async () => {
    const user = userEvent.setup();
    let addVariantCalled = false;
    renderEditPage('f-1', (url, init) => {
      if (init?.method === 'POST' && url.includes('/variants')) {
        addVariantCalled = true;
      }
    });

    await screen.findByText('Variants (1)');

    await user.click(screen.getByRole('button', { name: /add variant/i }));
    const dialog = within(screen.getByRole('dialog'));
    await user.type(dialog.getByLabelText('Name'), 'Red');
    await user.click(dialog.getByRole('button', { name: 'Red' }));
    await user.click(dialog.getByRole('button', { name: 'Add variant' }));

    await waitFor(() => expect(addVariantCalled).toBe(true));
  });
});
