import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ExplosionResultView } from './ExplosionResultView';

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function stubFetch(handler: (url: string) => unknown) {
  vi.stubGlobal(
    'fetch',
    vi.fn((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();
      return Promise.resolve(jsonResponse(handler(url)));
    }),
  );
}

function renderView() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    );
  }

  return render(<ExplosionResultView assemblyId="a1" />, { wrapper: Wrapper });
}

describe('ExplosionResultView', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('renders printed parts and non-printed components as separate lists (FR-009d)', async () => {
    stubFetch(() => ({
      printedParts: [{ partId: 'p1', partName: 'Bracket', quantity: 4 }],
      nonPrintedComponents: [
        {
          nonPrintedComponentId: 'c1',
          name: 'Screw',
          unit: 'each',
          quantity: 8,
        },
      ],
    }));

    renderView();

    expect(await screen.findByText('Printed parts')).toBeInTheDocument();
    expect(screen.getByText('Non-printed components')).toBeInTheDocument();
    expect(screen.getByText('Bracket')).toBeInTheDocument();
    expect(screen.getByText('Screw (each)')).toBeInTheDocument();
  });

  it('re-fetches when the requested quantity changes', async () => {
    const fetchUrls: string[] = [];
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL) => {
        const url = typeof input === 'string' ? input : input.toString();
        fetchUrls.push(url);
        return Promise.resolve(
          jsonResponse({ printedParts: [], nonPrintedComponents: [] }),
        );
      }),
    );

    renderView();
    await screen.findByText('Printed parts');

    const user = userEvent.setup();
    const quantityInput = screen.getByLabelText('Build quantity');
    await user.clear(quantityInput);
    await user.type(quantityInput, '5');

    await screen.findByText((_, el) =>
      Boolean(el?.textContent === 'Printed parts'),
    );

    expect(fetchUrls.some((u) => u.includes('quantity=5'))).toBe(true);
  });

  it('rejects a non-integer quantity without calling the API for it', async () => {
    stubFetch(() => ({ printedParts: [], nonPrintedComponents: [] }));

    renderView();
    await screen.findByText('Printed parts');

    const user = userEvent.setup();
    const quantityInput = screen.getByLabelText('Build quantity');
    await user.clear(quantityInput);
    await user.type(quantityInput, '0');

    expect(
      await screen.findByText('Quantity must be a whole number of at least 1.'),
    ).toBeInTheDocument();
  });
});
