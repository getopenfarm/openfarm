import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AddSpoolDialog } from './AddSpoolDialog';

function stubFetch(onPost: (url: string, body: unknown) => void) {
  vi.stubGlobal(
    'fetch',
    vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = input.toString();
      if (init?.method === 'POST') {
        onPost(url, JSON.parse(init.body as string));
        return Promise.resolve(
          new Response(JSON.stringify({ id: 'spool-new' }), {
            status: 200,
            headers: { 'Content-Type': 'application/json' },
          }),
        );
      }
      if (url.includes('/api/v1/filaments')) {
        return Promise.resolve(
          new Response(
            JSON.stringify({
              filaments: [
                {
                  id: 'f-1',
                  vendorName: 'Bambu Lab',
                  material: 'PLA Basic',
                  materialType: 'PLA',
                  variants: [
                    { id: 'var-1', colorName: 'Black', colorHex: '#18181b' },
                  ],
                },
              ],
            }),
            { status: 200, headers: { 'Content-Type': 'application/json' } },
          ),
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

function renderDialog(onPost: (url: string, body: unknown) => void) {
  stubFetch(onPost);
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <AddSpoolDialog />
    </QueryClientProvider>,
  );
}

describe('AddSpoolDialog', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('registers a spool for the selected filament variant', async () => {
    const user = userEvent.setup();
    let postedBody: unknown;
    renderDialog((_url, body) => {
      postedBody = body;
    });

    await user.click(screen.getByRole('button', { name: 'Add spool' }));

    await user.click(await screen.findByRole('combobox', { name: 'Filament' }));
    await user.click(
      await screen.findByRole('option', {
        name: 'Bambu Lab · PLA Basic · Black',
      }),
    );

    await user.click(screen.getByRole('button', { name: 'Add spool' }));

    await waitFor(() => {
      expect(postedBody).toMatchObject({
        filamentVariantId: 'var-1',
        spoolWeightGrams: 1000,
      });
    });
  });
});
