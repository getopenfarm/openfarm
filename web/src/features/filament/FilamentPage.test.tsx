import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type {
  FilamentResponse,
  VendorResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { FilamentPage } from './FilamentPage';

const FILAMENTS: FilamentResponse[] = [
  {
    id: 'f-bambu-pla-basic',
    vendorId: 'v-bambu',
    vendorName: 'Bambu Lab',
    material: 'PLA Basic',
    materialType: 'PLA',
    variants: [
      { id: 'var-1', colorName: 'Black', colorHex: '#18181b' },
      { id: 'var-2', colorName: 'White', colorHex: '#f4f4f5' },
    ],
  },
  {
    id: 'f-polymaker-polyterra',
    vendorId: 'v-polymaker',
    vendorName: 'Polymaker',
    material: 'PolyTerra PLA',
    materialType: 'PLA',
    variants: [{ id: 'var-3', colorName: 'Sakura', colorHex: '#f9a8d4' }],
  },
  {
    id: 'f-bambu-petg',
    vendorId: 'v-bambu',
    vendorName: 'Bambu Lab',
    material: 'PETG HF',
    materialType: 'PETG',
    variants: [{ id: 'var-4', colorName: 'Gray', colorHex: '#71717a' }],
  },
];

const VENDORS: VendorResponse[] = [
  { id: 'v-bambu', name: 'Bambu Lab' },
  { id: 'v-polymaker', name: 'Polymaker' },
];

function stubFetch() {
  vi.stubGlobal(
    'fetch',
    vi.fn((input: RequestInfo | URL) => {
      const url = input.toString();
      if (url.includes('/api/v1/filaments')) {
        return Promise.resolve(
          new Response(JSON.stringify({ filaments: FILAMENTS }), {
            status: 200,
            headers: { 'Content-Type': 'application/json' },
          }),
        );
      }
      if (url.includes('/api/v1/vendors')) {
        return Promise.resolve(
          new Response(JSON.stringify({ vendors: VENDORS }), {
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

function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <FilamentPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('FilamentPage', () => {
  beforeEach(() => {
    stubFetch();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('renders the catalog heading and subtitle counts', async () => {
    renderPage();

    expect(
      screen.getByRole('heading', { name: 'Filament' }),
    ).toBeInTheDocument();
    expect(
      await screen.findByText(/Catalog · 3 materials/),
    ).toBeInTheDocument();
  });

  it('renders vendor/material text and material type badges', async () => {
    renderPage();

    expect(
      await screen.findAllByText('Bambu Lab · PLA Basic'),
    ).not.toHaveLength(0);
    expect(screen.getByText('Polymaker · PolyTerra PLA')).toBeInTheDocument();
    expect(screen.getAllByText('PLA').length).toBeGreaterThan(0);
    expect(screen.getByText('PETG')).toBeInTheDocument();
  });

  it('has an Add filament button', () => {
    renderPage();
    expect(
      screen.getByRole('button', { name: 'Add filament' }),
    ).toBeInTheDocument();
  });

  it('shows vendors on the Vendors tab', async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getByRole('tab', { name: 'Vendors' }));

    expect(await screen.findByText('Bambu Lab')).toBeInTheDocument();
    expect(screen.getByText('Polymaker')).toBeInTheDocument();
  });
});
