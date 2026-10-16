import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AddVendorDialog } from './AddVendorDialog';

function stubFetch(onPost: (body: unknown) => void) {
  vi.stubGlobal(
    'fetch',
    vi.fn((_input: RequestInfo | URL, init?: RequestInit) => {
      if (init?.method === 'POST') {
        onPost(JSON.parse(init.body as string));
        return Promise.resolve(
          new Response(JSON.stringify({ id: 'v-new', name: 'New Vendor Co' }), {
            status: 200,
            headers: { 'Content-Type': 'application/json' },
          }),
        );
      }
      return Promise.resolve(
        new Response(JSON.stringify({ vendors: [] }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }),
      );
    }),
  );
}

function renderDialog(
  onAdded?: (vendor: { id?: string; name?: string }) => void,
) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <AddVendorDialog onAdded={onAdded} />
    </QueryClientProvider>,
  );
}

describe('AddVendorDialog', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('adds a vendor and reports it back via onAdded', async () => {
    const user = userEvent.setup();
    let postedBody: unknown;
    const onAdded = vi.fn();
    stubFetch((body) => {
      postedBody = body;
    });
    renderDialog(onAdded);

    await user.click(screen.getByRole('button', { name: 'Add vendor' }));
    await user.type(screen.getByLabelText('Name'), 'New Vendor Co');
    await user.click(screen.getByRole('button', { name: 'Add vendor' }));

    await waitFor(() => expect(postedBody).toEqual({ name: 'New Vendor Co' }));
    await waitFor(() =>
      expect(onAdded).toHaveBeenCalledWith(
        expect.objectContaining({ id: 'v-new', name: 'New Vendor Co' }),
      ),
    );
  });
});
