import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { VendorTable } from './VendorTable';

function stubFetch() {
  vi.stubGlobal(
    'fetch',
    vi.fn(() =>
      Promise.resolve(
        new Response(
          JSON.stringify({
            vendors: [
              { id: 'v-1', name: 'Bambu Lab' },
              { id: 'v-2', name: 'Polymaker' },
            ],
          }),
          { status: 200, headers: { 'Content-Type': 'application/json' } },
        ),
      ),
    ),
  );
}

function renderTable() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <VendorTable />
    </QueryClientProvider>,
  );
}

describe('VendorTable', () => {
  beforeEach(() => {
    stubFetch();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('lists vendors from the API', async () => {
    renderTable();

    expect(await screen.findByText('Bambu Lab')).toBeInTheDocument();
    expect(screen.getByText('Polymaker')).toBeInTheDocument();
  });
});
