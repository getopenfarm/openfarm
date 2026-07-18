import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Toaster } from '@/components/ui/sonner';
import { AddPrinterDialog } from './AddPrinterDialog';

function renderDialog() {
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

  return render(<AddPrinterDialog />, { wrapper: Wrapper });
}

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

const CATALOG_RESPONSE = {
  brands: [
    { brand: 'Bambu', models: ['X1', 'X1C', 'P1S'] },
    { brand: 'System', models: ['Simulation'] },
  ],
};

describe('AddPrinterDialog', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('populates Brand/Model options from the fetched catalog', async () => {
    const fetchMock = vi.fn((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/printers/catalog')) {
        return Promise.resolve(jsonResponse(CATALOG_RESPONSE));
      }
      return Promise.resolve(jsonResponse({}));
    });
    vi.stubGlobal('fetch', fetchMock);

    renderDialog();
    fireEvent.click(screen.getByRole('button', { name: 'Add printer' }));

    await waitFor(() => {
      const brandSelect = screen.getByLabelText('Brand') as HTMLSelectElement;
      expect(Array.from(brandSelect.options).map((o) => o.value)).toEqual([
        'Bambu',
        'System',
      ]);
    });

    const modelSelect = screen.getByLabelText('Model') as HTMLSelectElement;
    expect(Array.from(modelSelect.options).map((o) => o.value)).toEqual([
      'X1',
      'X1C',
      'P1S',
    ]);
  });

  it('disables the selects while the catalog is loading', async () => {
    let resolveCatalog!: (value: Response) => void;
    const fetchMock = vi.fn((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/printers/catalog')) {
        return new Promise<Response>((resolve) => {
          resolveCatalog = resolve;
        });
      }
      return Promise.resolve(jsonResponse({}));
    });
    vi.stubGlobal('fetch', fetchMock);

    renderDialog();
    fireEvent.click(screen.getByRole('button', { name: 'Add printer' }));

    expect(screen.getByLabelText('Brand')).toBeDisabled();
    expect(screen.getByLabelText('Model')).toBeDisabled();

    resolveCatalog(jsonResponse(CATALOG_RESPONSE));
    await waitFor(() =>
      expect(screen.getByLabelText('Brand')).not.toBeDisabled(),
    );
  });

  it('disables submission and shows an error state when the catalog fails to load', async () => {
    const fetchMock = vi.fn((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/printers/catalog')) {
        return Promise.resolve(jsonResponse({ title: 'Server error' }, 500));
      }
      return Promise.resolve(jsonResponse({}));
    });
    vi.stubGlobal('fetch', fetchMock);

    renderDialog();
    fireEvent.click(screen.getByRole('button', { name: 'Add printer' }));

    expect(
      await screen.findByText(/Couldn't load the printer catalog/),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: /add printer/i, hidden: false }),
    ).toBeDisabled();
  });
});
